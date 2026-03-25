using Core.Models;

namespace Core.Dispatch;

public interface IFireBoxAiDispatcher
{
    Task<List<ModelInfo>> ListModelsAsync();
    Task<Result<ChatCompletionResponse>> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct);
    IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken ct);
    Task<Result<EmbeddingResponse>> CreateEmbeddingsAsync(EmbeddingRequest request, CancellationToken ct);
    Task<Result<FunctionCallResponse>> CallFunctionAsync(FunctionCallRequest request, CancellationToken ct);
}
