using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
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

    public async Task<List<ModelInfo>> ListModelsAsync()
    {
        var routes = await _configRepo.ListRoutesAsync();
        var providers = await _configRepo.ListProvidersAsync();
        var result = new List<ModelInfo>(routes.Count);

        foreach (var route in routes)
        {
            var available = false;
            var candidates = GetOrderedCandidates(route);

            foreach (var candidate in candidates)
            {
                var provider = providers.FirstOrDefault(p => p.Id == candidate.ProviderId);
                if (provider is null)
                    continue;

                if (!ProviderSupportsRoute(provider, candidate.ModelId, route))
                    continue;

                try
                {
                    var apiKey = _keyStore.Decrypt(provider.EncryptedApiKey);
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        available = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping availability for provider {ProviderId}/{ModelId}", provider.Id, candidate.ModelId);
                }
            }

            result.Add(new ModelInfo(
                route.RouteId,
                new ModelCapabilities(
                    route.Reasoning,
                    route.ToolCalling,
                    ParseFormatsMask(route.InputFormatsMask),
                    ParseFormatsMask(route.OutputFormatsMask)),
                available));
        }

        return result;
    }

    public async Task<Result<ChatCompletionResponse>> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        var validationError = CapabilityRequestValidator.Validate(request);
        if (validationError is not null)
            return new Result<ChatCompletionResponse>(null, validationError);

        var route = await _configRepo.GetRouteByRouteIdAsync(request.ModelId);
        if (route is null)
            return new Result<ChatCompletionResponse>(null, $"No route configured for requested model '{request.ModelId}'.");

        var (attempts, preparationErrors) = await PrepareCandidatesAsync(route);
        if (attempts.Count == 0)
            return new Result<ChatCompletionResponse>(null, BuildUnavailableModelError(request.ModelId, preparationErrors));

        var candidateErrors = new List<string>(preparationErrors);
        foreach (var attempt in attempts)
        {
            try
            {
                var response = await attempt.Gateway.ChatCompletionAsync(
                    attempt.ModelId,
                    request.Messages,
                    request.Temperature,
                    request.MaxOutputTokens,
                    request.ReasoningEffort,
                    ct);

                var result = response with { ModelId = request.ModelId };
                await RecordUsageAsync(attempt.Provider, attempt.ModelId, result.Usage);
                return new Result<ChatCompletionResponse>(result, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ChatCompletion failed on {Provider}/{Model}", attempt.Provider.Name, attempt.ModelId);
                candidateErrors.Add($"{attempt.Provider.Name}/{attempt.ModelId}: {ex.Message}");
            }
        }

        return new Result<ChatCompletionResponse>(null, BuildUnavailableModelError(request.ModelId, candidateErrors));
    }

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ChatStreamEvent>();
        _ = Task.Run(() => ProduceChatCompletionStreamAsync(request, channel.Writer, ct), CancellationToken.None);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }

    private async Task ProduceChatCompletionStreamAsync(
        ChatCompletionRequest request,
        ChannelWriter<ChatStreamEvent> writer,
        CancellationToken ct)
    {
        try
        {
            var validationError = CapabilityRequestValidator.Validate(request);
            if (validationError is not null)
            {
                await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Error, Error: validationError), CancellationToken.None);
                return;
            }

            var route = await _configRepo.GetRouteByRouteIdAsync(request.ModelId);
            if (route is null)
            {
                await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Error, Error: $"No route configured for requested model '{request.ModelId}'."), CancellationToken.None);
                return;
            }

            var (attempts, preparationErrors) = await PrepareCandidatesAsync(route);
            if (attempts.Count == 0)
            {
                await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Error, Error: BuildUnavailableModelError(request.ModelId, preparationErrors)), CancellationToken.None);
                return;
            }

            var candidateErrors = new List<string>(preparationErrors);
            foreach (var attempt in attempts)
            {
                var fullContent = new StringBuilder();
                var fullReasoning = new StringBuilder();
                var promptTokens = 0L;
                var completionTokens = 0L;
                var started = false;
                var finishReason = "stop";

                try
                {
                    var stream = attempt.Gateway.ChatCompletionStreamAsync(
                        attempt.ModelId,
                        request.Messages,
                        request.Temperature,
                        request.MaxOutputTokens,
                        request.ReasoningEffort,
                        ct);

                    await using var enumerator = stream.GetAsyncEnumerator(ct);

                    bool hasChunk;
                    try
                    {
                        hasChunk = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Stream failed before start on {Provider}/{Model}", attempt.Provider.Name, attempt.ModelId);
                        candidateErrors.Add($"{attempt.Provider.Name}/{attempt.ModelId}: {ex.Message}");
                        continue;
                    }

                    started = true;
                    await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Started), ct);

                    while (hasChunk)
                    {
                        var chunk = enumerator.Current;
                        switch (chunk.Type)
                        {
                            case StreamChunkType.Delta:
                                if (!string.IsNullOrEmpty(chunk.DeltaText))
                                {
                                    fullContent.Append(chunk.DeltaText);
                                    await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Delta, DeltaText: chunk.DeltaText), ct);
                                }
                                break;
                            case StreamChunkType.ReasoningDelta:
                                if (!string.IsNullOrEmpty(chunk.ReasoningText))
                                {
                                    fullReasoning.Append(chunk.ReasoningText);
                                    await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.ReasoningDelta, ReasoningText: chunk.ReasoningText), ct);
                                }
                                break;
                            case StreamChunkType.Usage:
                                promptTokens = chunk.PromptTokens;
                                completionTokens = chunk.CompletionTokens;
                                if (promptTokens != 0 || completionTokens != 0)
                                {
                                    await writer.WriteAsync(
                                        new ChatStreamEvent(
                                            0,
                                            ChatStreamEventType.Usage,
                                            Usage: new Usage(promptTokens, completionTokens, promptTokens + completionTokens)),
                                        ct);
                                }
                                break;
                            case StreamChunkType.Done:
                                if (!string.IsNullOrWhiteSpace(chunk.FinishReason))
                                    finishReason = chunk.FinishReason;
                                break;
                        }

                        hasChunk = await enumerator.MoveNextAsync();
                    }

                    var reasoningText = fullReasoning.Length > 0 ? fullReasoning.ToString() : null;
                    var usage = new Usage(promptTokens, completionTokens, promptTokens + completionTokens);
                    var response = new ChatCompletionResponse(
                        request.ModelId,
                        new ChatMessage("assistant", fullContent.ToString()),
                        reasoningText,
                        usage,
                        finishReason);

                    await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Completed, Response: response), ct);
                    await RecordUsageAsync(attempt.Provider, attempt.ModelId, usage);
                    return;
                }
                catch (OperationCanceledException)
                {
                    await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Cancelled), CancellationToken.None);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stream failed on {Provider}/{Model}", attempt.Provider.Name, attempt.ModelId);
                    if (started)
                    {
                        await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Error, Error: ex.Message), CancellationToken.None);
                        return;
                    }

                    candidateErrors.Add($"{attempt.Provider.Name}/{attempt.ModelId}: {ex.Message}");
                }
            }

            await writer.WriteAsync(new ChatStreamEvent(0, ChatStreamEventType.Error, Error: BuildUnavailableModelError(request.ModelId, candidateErrors)), CancellationToken.None);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    public async Task<Result<EmbeddingResponse>> CreateEmbeddingsAsync(EmbeddingRequest request, CancellationToken ct)
    {
        var validationError = CapabilityRequestValidator.Validate(request);
        if (validationError is not null)
            return new Result<EmbeddingResponse>(null, validationError);

        var route = await _configRepo.GetRouteByRouteIdAsync(request.ModelId);
        if (route is null)
            return new Result<EmbeddingResponse>(null, $"No route configured for requested model '{request.ModelId}'.");

        var (attempts, preparationErrors) = await PrepareCandidatesAsync(route);
        if (attempts.Count == 0)
            return new Result<EmbeddingResponse>(null, BuildUnavailableModelError(request.ModelId, preparationErrors));

        var candidateErrors = new List<string>(preparationErrors);
        foreach (var attempt in attempts)
        {
            try
            {
                var response = await attempt.Gateway.CreateEmbeddingsAsync(attempt.ModelId, request.Input, ct);
                var result = response with { ModelId = request.ModelId };
                await RecordUsageAsync(attempt.Provider, attempt.ModelId, result.Usage);
                return new Result<EmbeddingResponse>(result, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embeddings failed on {Provider}/{Model}", attempt.Provider.Name, attempt.ModelId);
                candidateErrors.Add($"{attempt.Provider.Name}/{attempt.ModelId}: {ex.Message}");
            }
        }

        return new Result<EmbeddingResponse>(null, BuildUnavailableModelError(request.ModelId, candidateErrors));
    }

    public async Task<Result<FunctionCallResponse>> CallFunctionAsync(FunctionCallRequest request, CancellationToken ct)
    {
        var validationError = CapabilityRequestValidator.Validate(request);
        if (validationError is not null)
            return new Result<FunctionCallResponse>(null, validationError);

        var route = await _configRepo.GetRouteByRouteIdAsync(request.ModelId);
        if (route is null)
            return new Result<FunctionCallResponse>(null, $"No route configured for requested model '{request.ModelId}'.");

        var (attempts, preparationErrors) = await PrepareCandidatesAsync(route);
        if (attempts.Count == 0)
            return new Result<FunctionCallResponse>(null, BuildUnavailableModelError(request.ModelId, preparationErrors));

        var candidateErrors = new List<string>(preparationErrors);
        foreach (var attempt in attempts)
        {
            try
            {
                var response = await attempt.Gateway.CallFunctionAsync(
                    attempt.ModelId,
                    request.FunctionName,
                    request.FunctionDescription,
                    request.InputJson,
                    request.InputSchemaJson,
                    request.OutputSchemaJson,
                    request.Temperature,
                    request.MaxOutputTokens,
                    ct);

                var result = response with { ModelId = request.ModelId };
                await RecordUsageAsync(attempt.Provider, attempt.ModelId, result.Usage);
                return new Result<FunctionCallResponse>(result, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Function call failed on {Provider}/{Model}", attempt.Provider.Name, attempt.ModelId);
                candidateErrors.Add($"{attempt.Provider.Name}/{attempt.ModelId}: {ex.Message}");
            }
        }

        return new Result<FunctionCallResponse>(null, BuildUnavailableModelError(request.ModelId, candidateErrors));
    }

    private async Task<(List<CandidateAttempt> Attempts, List<string> PreparationErrors)> PrepareCandidatesAsync(RouteRuleEntity route)
    {
        var providers = await _configRepo.ListProvidersAsync();
        var attempts = new List<CandidateAttempt>();
        var errors = new List<string>();

        foreach (var candidate in GetOrderedCandidates(route))
        {
            var provider = providers.FirstOrDefault(p => p.Id == candidate.ProviderId);
            if (provider is null)
            {
                errors.Add($"provider {candidate.ProviderId}/{candidate.ModelId}: provider not found");
                continue;
            }

            if (!ProviderSupportsRoute(provider, candidate.ModelId, route))
            {
                errors.Add($"{provider.Name}/{candidate.ModelId}: route capabilities are not supported by provider type '{provider.ProviderType}'");
                continue;
            }

            string apiKey;
            try
            {
                apiKey = _keyStore.Decrypt(provider.EncryptedApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping provider {Provider}/{Model} because the API key could not be decrypted", provider.Name, candidate.ModelId);
                errors.Add($"{provider.Name}/{candidate.ModelId}: {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                errors.Add($"{provider.Name}/{candidate.ModelId}: provider API key is empty");
                continue;
            }

            attempts.Add(new CandidateAttempt(
                provider,
                candidate.ModelId,
                _gatewayFactory.Create(provider.ProviderType, apiKey, provider.BaseUrl)));
        }

        return (attempts, errors);
    }

    private List<RouteCandidateInfo> GetOrderedCandidates(RouteRuleEntity route)
    {
        var candidates = _configRepo.GetCandidates(route);
        if (string.Equals(route.Strategy, FireBoxRouteStrategies.Random, StringComparison.OrdinalIgnoreCase))
            return candidates.OrderBy(_ => Random.Shared.Next()).ToList();

        return candidates;
    }

    private bool ProviderSupportsRoute(ProviderConfigEntity provider, string candidateModelId, RouteRuleEntity route)
    {
        var enabledModels = _configRepo.GetEnabledModelIds(provider);
        if (!enabledModels.Contains(candidateModelId, StringComparer.OrdinalIgnoreCase))
            return false;

        return CheckCapabilitySupported(provider.ProviderType, route);
    }

    private async Task RecordUsageAsync(ProviderConfigEntity provider, string modelId, Usage usage)
    {
        try
        {
            await _statsRepo.RecordUsageAsync(
                provider.Id,
                provider.ProviderType,
                modelId,
                usage.PromptTokens,
                usage.CompletionTokens,
                0m);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage stats");
        }
    }

    private static string BuildUnavailableModelError(string modelId, List<string> errors)
    {
        if (errors.Count == 0)
            return $"No available provider for requested model '{modelId}'.";

        return $"No available provider for requested model '{modelId}'. {string.Join("; ", errors)}";
    }

    private static List<MediaFormat>? ParseFormatsMask(int mask)
    {
        if (mask == 0)
            return null;

        var formats = new List<MediaFormat>();
        if ((mask & MediaFormatMask.ImageBit) != 0) formats.Add(MediaFormat.Image);
        if ((mask & MediaFormatMask.VideoBit) != 0) formats.Add(MediaFormat.Video);
        if ((mask & MediaFormatMask.AudioBit) != 0) formats.Add(MediaFormat.Audio);
        return formats;
    }

    private static bool CheckCapabilitySupported(string providerType, RouteRuleEntity route)
    {
        return providerType switch
        {
            FireBoxProviderTypes.Anthropic => !route.ToolCalling && (route.InputFormatsMask & (MediaFormatMask.VideoBit | MediaFormatMask.AudioBit)) == 0,
            FireBoxProviderTypes.Gemini => (route.InputFormatsMask & MediaFormatMask.AudioBit) == 0,
            FireBoxProviderTypes.OpenAI => (route.InputFormatsMask & (MediaFormatMask.VideoBit | MediaFormatMask.AudioBit)) == 0,
            _ => false,
        };
    }

    private sealed record CandidateAttempt(
        ProviderConfigEntity Provider,
        string ModelId,
        IProviderGateway Gateway);
}
