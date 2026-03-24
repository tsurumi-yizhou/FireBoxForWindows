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

    // --- Chat Completion with single candidate execution ---

    public async Task<ChatCompletionResult> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        var (gateway, provider, modelId) = await ResolveCandidateAsync(request.VirtualModelId);
        var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);

        try
        {
            var response = await gateway.ChatCompletionAsync(
                modelId, request.Messages, request.Attachments,
                request.Temperature, request.MaxOutputTokens, ct);

            var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
            await RecordUsageAsync(provider, modelId, result.Usage);
            return new ChatCompletionResult(result, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChatCompletion failed on {Provider}/{Model}", provider.Name, modelId);
            return new ChatCompletionResult(null, CreateProviderError(ex, provider, modelId));
        }
    }

    // --- Streaming with single candidate execution ---

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ChatStreamEvent>();
        _ = Task.Run(() => StreamSingleCandidateAsync(request, channel.Writer, ct), ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }

    private async Task StreamSingleCandidateAsync(
        ChatCompletionRequest request,
        System.Threading.Channels.ChannelWriter<ChatStreamEvent> writer,
        CancellationToken ct)
    {
        var requestId = Interlocked.Increment(ref s_requestIdCounter);

        try
        {
            var (gateway, provider, modelId) = await ResolveCandidateAsync(request.VirtualModelId);
            var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);
            var stream = gateway.ChatCompletionStreamAsync(
                modelId, request.Messages, request.Attachments,
                request.Temperature, request.MaxOutputTokens, ct);

            var fullContent = new System.Text.StringBuilder();
            var fullReasoning = new System.Text.StringBuilder();
            long promptTokens = 0, completionTokens = 0;
            string? finishReason = null;
            var startedEmitted = false;

            try
            {
                await foreach (var chunk in stream.WithCancellation(ct))
                {
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
                            break;
                        case StreamChunkType.ReasoningDelta:
                            fullReasoning.Append(chunk.ReasoningText);
                            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.ReasoningDelta, ReasoningText: chunk.ReasoningText), ct);
                            break;
                        case StreamChunkType.Usage:
                            promptTokens = chunk.PromptTokens;
                            completionTokens = chunk.CompletionTokens;
                            var usage = new Usage(promptTokens, completionTokens, promptTokens + completionTokens);
                            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Usage, Usage: usage), ct);
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stream failed on {Provider}/{Model}", provider.Name, modelId);
                await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Error,
                    Error: CreateProviderError(ex, provider, modelId)), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Cancelled), CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            await writer.WriteAsync(new ChatStreamEvent(requestId, ChatStreamEventType.Error,
                Error: new FireBoxError(FireBoxError.NoCandidate, ex.Message, null, null)), CancellationToken.None);
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

    // --- Embeddings with single candidate execution ---

    public async Task<EmbeddingResult> CreateEmbeddingsAsync(EmbeddingRequest request, CancellationToken ct)
    {
        var (gateway, provider, modelId) = await ResolveCandidateAsync(request.VirtualModelId);
        var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);

        try
        {
            var response = await gateway.CreateEmbeddingsAsync(modelId, request.Input, ct);
            var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
            await RecordUsageAsync(provider, modelId, result.Usage);
            return new EmbeddingResult(result, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embeddings failed on {Provider}/{Model}", provider.Name, modelId);
            return new EmbeddingResult(null, CreateProviderError(ex, provider, modelId));
        }
    }

    // --- Function Call with single candidate execution ---

    public async Task<FunctionCallResult> CallFunctionAsync(FunctionCallRequest request, CancellationToken ct)
    {
        var (gateway, provider, modelId) = await ResolveCandidateAsync(request.VirtualModelId);
        var selection = new ProviderSelection(provider.Id, provider.ProviderType, provider.Name, modelId);

        try
        {
            var response = await gateway.CallFunctionAsync(
                modelId, request.FunctionName, request.FunctionDescription,
                request.InputJson, request.InputSchemaJson, request.OutputSchemaJson,
                request.Temperature, request.MaxOutputTokens, ct);

            var result = response with { VirtualModelId = request.VirtualModelId, Selection = selection };
            await RecordUsageAsync(provider, modelId, result.Usage);
            return new FunctionCallResult(result, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Function call failed on {Provider}/{Model}", provider.Name, modelId);
            return new FunctionCallResult(null, CreateProviderError(ex, provider, modelId));
        }
    }

    // --- Candidate resolution ---

    private static long s_requestIdCounter;

    /// <summary>
    /// Resolves one viable candidate for a virtual model according to the configured selection strategy.
    /// Filters by: model in enabled list, capability supported.
    /// </summary>
    private async Task<(IProviderGateway Gateway, ProviderConfigEntity Provider, string ModelId)> ResolveCandidateAsync(string virtualModelId)
    {
        var routes = await _configRepo.ListRoutesAsync();
        var route = routes.FirstOrDefault(r => r.VirtualModelId == virtualModelId)
            ?? throw new InvalidOperationException($"No route configured for '{virtualModelId}'");

        var providers = await _configRepo.ListProvidersAsync();
        var candidates = _configRepo.GetCandidates(route);

        var orderedCandidates = string.Equals(route.Strategy, "Random", StringComparison.OrdinalIgnoreCase)
            ? candidates.OrderBy(_ => Random.Shared.Next()).ToList()
            : candidates;

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
            return (gateway, provider, candidate.ModelId);
        }

        throw new InvalidOperationException($"No available candidates for '{virtualModelId}'");
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

    private static FireBoxError CreateProviderError(Exception ex, ProviderConfigEntity provider, string modelId) =>
        new(FireBoxError.ProviderError, ex.Message, provider.ProviderType, modelId);

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
