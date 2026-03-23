using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.Dispatch;
using Core.Models;
using Microsoft.Extensions.Logging;
using Service.Data;
using Service.Data.Entities;
using Service.Providers;

namespace Service.Dispatch;

public sealed class FireBoxAiDispatcher : IFireBoxAiDispatcher
{
    private readonly FireBoxConfigRepository _configRepo;
    private readonly FireBoxStatsRepository _statsRepo;
    private readonly ProviderGatewayFactory _gatewayFactory;
    private readonly SecureKeyStore _keyStore;
    private readonly ILogger<FireBoxAiDispatcher> _logger;

    public FireBoxAiDispatcher(
        FireBoxConfigRepository configRepo,
        FireBoxStatsRepository statsRepo,
        ProviderGatewayFactory gatewayFactory,
        SecureKeyStore keyStore,
        ILogger<FireBoxAiDispatcher> logger)
    {
        _configRepo = configRepo;
        _statsRepo = statsRepo;
        _gatewayFactory = gatewayFactory;
        _keyStore = keyStore;
        _logger = logger;
    }

    public async Task<List<VirtualModelInfo>> ListVirtualModelsAsync()
    {
        var routes = await _configRepo.ListRoutesAsync();
        var providers = await _configRepo.ListProvidersAsync();
        var result = new List<VirtualModelInfo>();

        foreach (var route in routes)
        {
            var candidates = _configRepo.GetCandidates(route);
            var enabledModelIds = new HashSet<string>();

            var candidateInfos = candidates.Select(c =>
            {
                var provider = providers.FirstOrDefault(p => p.Id == c.ProviderId);
                if (provider is not null)
                    enabledModelIds = _configRepo.GetEnabledModelIds(provider).ToHashSet();

                var modelEnabled = enabledModelIds.Contains(c.ModelId);
                var capSupported = CheckCapabilitySupported(provider?.ProviderType, route);

                return new ModelCandidateInfo(
                    c.ProviderId,
                    provider?.ProviderType ?? "Unknown",
                    provider?.Name ?? "Unknown",
                    provider?.BaseUrl ?? string.Empty,
                    c.ModelId,
                    modelEnabled,
                    capSupported);
            }).ToList();

            var available = candidateInfos.Any(c => c.EnabledInConfig && c.CapabilitySupported);

            result.Add(new VirtualModelInfo(
                route.VirtualModelId,
                route.Strategy,
                new ModelCapabilities(
                    route.Reasoning, route.ToolCalling,
                    ParseFormatsMask(route.InputFormatsMask),
                    ParseFormatsMask(route.OutputFormatsMask)),
                candidateInfos,
                available));
        }
        return result;
    }

    public async Task<List<ModelCandidateInfo>> GetModelCandidatesAsync(string virtualModelId)
    {
        var models = await ListVirtualModelsAsync();
        return models.FirstOrDefault(m => m.VirtualModelId == virtualModelId)?.Candidates ?? [];
    }

    // --- Chat Completion with real failover ---

    public async Task<ChatCompletionResult> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        var resolved = await ResolveCandidatesAsync(request.VirtualModelId);
        Exception? lastException = null;

        foreach (var (gateway, provider, modelId) in resolved)
        {
            try
            {
                var response = await gateway.ChatCompletionAsync(
                    modelId, request.Messages, request.Attachments,
                    request.Temperature, request.MaxOutputTokens, ct);

                var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);
                var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
                await RecordUsageAsync(provider, modelId, result.Usage);
                return new ChatCompletionResult(result, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ChatCompletion failed on {Provider}/{Model}, trying next candidate",
                    provider.Name, modelId);
                lastException = ex;
            }
        }

        return new ChatCompletionResult(null, new FireBoxError(
            FireBoxError.NoCandidate,
            $"All candidates failed for '{request.VirtualModelId}': {lastException?.Message}",
            null, null));
    }

    // --- Streaming with real failover ---

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ChatStreamEvent>();
        _ = Task.Run(() => StreamWithFailoverAsync(request, channel.Writer, ct), ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }

    private async Task StreamWithFailoverAsync(
        ChatCompletionRequest request,
        System.Threading.Channels.ChannelWriter<ChatStreamEvent> writer,
        CancellationToken ct)
    {
        var requestId = Interlocked.Increment(ref s_requestIdCounter);
        Exception? lastException = null;
        var startedEmitted = false;

        try
        {
            var resolved = await ResolveCandidatesAsync(request.VirtualModelId);

            foreach (var (gateway, provider, modelId) in resolved)
            {
                var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);

                IAsyncEnumerable<StreamChunk> stream;
                try
                {
                    stream = gateway.ChatCompletionStreamAsync(
                        modelId, request.Messages, request.Attachments,
                        request.Temperature, request.MaxOutputTokens, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stream setup failed on {Provider}/{Model}", provider.Name, modelId);
                    lastException = ex;
                    continue;
                }

                var fullContent = new System.Text.StringBuilder();
                var fullReasoning = new System.Text.StringBuilder();
                long promptTokens = 0, completionTokens = 0;
                string? finishReason = null;
                var succeeded = false;
                var anyEventEmitted = false;

                try
                {
                    await foreach (var chunk in stream.WithCancellation(ct))
                    {
                        // Defer Started until the first real content event — ensures
                        // the reported provider matches the one that actually produces output.
                        if (!startedEmitted)
                        {
                            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Started, Selection: selection), ct);
                            startedEmitted = true;
                        }

                        switch (chunk.Type)
                        {
                            case StreamChunkType.Delta:
                                fullContent.Append(chunk.DeltaText);
                                await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Delta, DeltaText: chunk.DeltaText), ct);
                                anyEventEmitted = true;
                                break;
                            case StreamChunkType.ReasoningDelta:
                                fullReasoning.Append(chunk.ReasoningText);
                                await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.ReasoningDelta, ReasoningText: chunk.ReasoningText), ct);
                                anyEventEmitted = true;
                                break;
                            case StreamChunkType.Usage:
                                promptTokens = chunk.PromptTokens;
                                completionTokens = chunk.CompletionTokens;
                                var usage = new Usage(promptTokens, completionTokens, promptTokens + completionTokens);
                                await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Usage, Usage: usage), ct);
                                anyEventEmitted = true;
                                break;
                            case StreamChunkType.Done:
                                finishReason = chunk.FinishReason;
                                break;
                        }
                    }

                    var finalUsage = new Usage(promptTokens, completionTokens, promptTokens + completionTokens);
                    var reasoning = fullReasoning.Length > 0 ? fullReasoning.ToString() : null;
                    var completedResponse = new ChatCompletionResponse(
                        request.VirtualModelId,
                        new ChatMessage("assistant", fullContent.ToString()),
                        reasoning, selection, finalUsage, finishReason ?? "stop");

                    await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Completed, Response: completedResponse), ct);
                    await RecordUsageAsync(provider, modelId, finalUsage);
                    succeeded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stream failed mid-flight on {Provider}/{Model}", provider.Name, modelId);
                    lastException = ex;
                    // If any content/reasoning/usage events were already emitted, can't failover cleanly
                    if (anyEventEmitted)
                    {
                        await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Error,
                            Error: new FireBoxError(FireBoxError.ProviderError, ex.Message, provider.ProviderType, modelId)), ct);
                        return;
                    }
                }

                if (succeeded) return;
            }

            // All candidates exhausted
            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Error,
                Error: new FireBoxError(FireBoxError.NoCandidate,
                    $"All candidates failed for '{request.VirtualModelId}': {lastException?.Message}",
                    null, null)), ct);
        }
        catch (OperationCanceledException)
        {
            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Cancelled), CancellationToken.None);
        }
        catch (Exception ex)
        {
            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Error,
                Error: new FireBoxError(FireBoxError.Internal, ex.Message, null, null)), CancellationToken.None);
        }
        finally
        {
            writer.Complete();
        }
    }

    // --- Embeddings with real failover ---

    public async Task<EmbeddingResult> CreateEmbeddingsAsync(EmbeddingRequest request, CancellationToken ct)
    {
        var resolved = await ResolveCandidatesAsync(request.VirtualModelId);
        Exception? lastException = null;

        foreach (var (gateway, provider, modelId) in resolved)
        {
            try
            {
                var response = await gateway.CreateEmbeddingsAsync(modelId, request.Input, ct);
                var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);
                var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
                await RecordUsageAsync(provider, modelId, result.Usage);
                return new EmbeddingResult(result, null);
            }
            catch (NotSupportedException)
            {
                _logger.LogDebug("Embeddings not supported by {Provider}, skipping", provider.ProviderType);
                lastException = new NotSupportedException($"{provider.ProviderType} does not support embeddings");
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embeddings failed on {Provider}/{Model}", provider.Name, modelId);
                lastException = ex;
            }
        }

        return new EmbeddingResult(null, new FireBoxError(
            FireBoxError.NoCandidate,
            $"All candidates failed for '{request.VirtualModelId}': {lastException?.Message}",
            null, null));
    }

    // --- Function Call with real failover ---

    public async Task<FunctionCallResult> CallFunctionAsync(FunctionCallRequest request, CancellationToken ct)
    {
        var resolved = await ResolveCandidatesAsync(request.VirtualModelId);
        Exception? lastException = null;

        foreach (var (gateway, provider, modelId) in resolved)
        {
            try
            {
                var response = await gateway.CallFunctionAsync(
                    modelId, request.FunctionName, request.FunctionDescription,
                    request.InputJson, request.InputSchemaJson, request.OutputSchemaJson,
                    request.Temperature, request.MaxOutputTokens, ct);

                var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);
                var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
                await RecordUsageAsync(provider, modelId, result.Usage);
                return new FunctionCallResult(result, null);
            }
            catch (NotSupportedException)
            {
                _logger.LogDebug("Function call not supported by {Provider}, skipping", provider.ProviderType);
                lastException = new NotSupportedException($"{provider.ProviderType} does not support function calls");
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Function call failed on {Provider}/{Model}", provider.Name, modelId);
                lastException = ex;
            }
        }

        return new FunctionCallResult(null, new FireBoxError(
            FireBoxError.NoCandidate,
            $"All candidates failed for '{request.VirtualModelId}': {lastException?.Message}",
            null, null));
    }

    // --- Candidate resolution ---

    private static long s_requestIdCounter;

    /// <summary>
    /// Resolves all viable candidates for a virtual model, ordered by strategy.
    /// Filters by: model in enabled list, capability supported.
    /// </summary>
    private async Task<List<(IProviderGateway Gateway, ProviderConfigEntity Provider, string ModelId)>> ResolveCandidatesAsync(string virtualModelId)
    {
        var routes = await _configRepo.ListRoutesAsync();
        var route = routes.FirstOrDefault(r => r.VirtualModelId == virtualModelId)
            ?? throw new InvalidOperationException($"No route configured for '{virtualModelId}'");

        var providers = await _configRepo.ListProvidersAsync();
        var candidates = _configRepo.GetCandidates(route);

        // Order by strategy
        var orderedCandidates = route.Strategy == "Random"
            ? candidates.OrderBy(_ => Random.Shared.Next()).ToList()
            : candidates; // Failover: use configured order

        var result = new List<(IProviderGateway, ProviderConfigEntity, string)>();

        foreach (var candidate in orderedCandidates)
        {
            var provider = providers.FirstOrDefault(p => p.Id == candidate.ProviderId);
            if (provider is null) continue;

            // Check enabled model list
            var enabledModels = _configRepo.GetEnabledModelIds(provider);
            if (!enabledModels.Contains(candidate.ModelId)) continue;

            // Check capability support
            if (!CheckCapabilitySupported(provider.ProviderType, route)) continue;

            var apiKey = _keyStore.Decrypt(provider.EncryptedApiKey);
            if (string.IsNullOrEmpty(apiKey)) continue;

            var gateway = _gatewayFactory.Create(provider.ProviderType, apiKey, provider.BaseUrl);
            result.Add((gateway, provider, candidate.ModelId));
        }

        if (result.Count == 0)
            throw new InvalidOperationException($"No available candidates for '{virtualModelId}'");

        return result;
    }

    /// <summary>
    /// Checks if a provider type supports the capabilities required by the route.
    /// </summary>
    private static bool CheckCapabilitySupported(string? providerType, RouteRuleEntity route)
    {
        if (providerType is null) return false;

        switch (providerType)
        {
            case "Anthropic":
                // Anthropic does not support function/tool calling
                if (route.ToolCalling) return false;
                // Anthropic supports image input but not video/audio
                if ((route.InputFormatsMask & 0b110) != 0) return false; // video or audio
                return true;

            case "Gemini":
                // Gemini supports image/video via REST but not audio input well
                if ((route.InputFormatsMask & 0b100) != 0) return false; // audio
                return true;

            case "OpenAI":
                // OpenAI supports image input, not video/audio natively
                if ((route.InputFormatsMask & 0b110) != 0) return false; // video or audio
                return true;

            default:
                return true;
        }
    }

    private async Task RecordUsageAsync(ProviderConfigEntity provider, string modelId, Usage usage)
    {
        try
        {
            await _statsRepo.RecordUsageAsync(
                provider.Id, provider.ProviderType, modelId,
                usage.PromptTokens, usage.CompletionTokens, 0m);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage stats");
        }
    }

    private static List<ModelMediaFormat>? ParseFormatsMask(int mask)
    {
        if (mask == 0) return null;
        var formats = new List<ModelMediaFormat>();
        if ((mask & 1) != 0) formats.Add(ModelMediaFormat.Image);
        if ((mask & 2) != 0) formats.Add(ModelMediaFormat.Video);
        if ((mask & 4) != 0) formats.Add(ModelMediaFormat.Audio);
        return formats;
    }
}
