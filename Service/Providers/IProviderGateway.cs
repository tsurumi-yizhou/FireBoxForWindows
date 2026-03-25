using Core.Models;

namespace Service.Providers;

public interface IProviderGateway
{
    string ProviderType { get; }

    Task<ChatCompletionResponse> ChatCompletionAsync(
        string modelId, List<ChatMessage> messages,
        float temperature, int maxOutputTokens, ReasoningEffort reasoningEffort, CancellationToken ct);

    IAsyncEnumerable<StreamChunk> ChatCompletionStreamAsync(
        string modelId, List<ChatMessage> messages,
        float temperature, int maxOutputTokens, ReasoningEffort reasoningEffort, CancellationToken ct);

    Task<EmbeddingResponse> CreateEmbeddingsAsync(
        string modelId, List<string> input, CancellationToken ct);

    Task<FunctionCallResponse> CallFunctionAsync(
        string modelId, string functionName, string functionDescription,
        string inputJson, string inputSchemaJson, string outputSchemaJson,
        float temperature, int maxOutputTokens, CancellationToken ct);

    Task<List<string>> ListModelsAsync(CancellationToken ct);
}

public sealed record StreamChunk(
    StreamChunkType Type,
    string? DeltaText = null,
    string? ReasoningText = null,
    string? FinishReason = null,
    long PromptTokens = 0,
    long CompletionTokens = 0);

public enum StreamChunkType
{
    Delta,
    ReasoningDelta,
    Usage,
    Done,
}
