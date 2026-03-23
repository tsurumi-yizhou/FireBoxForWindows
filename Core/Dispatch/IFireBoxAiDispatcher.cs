using Core.Models;

namespace Core.Dispatch;

public interface IFireBoxAiDispatcher
{
    Task<List<VirtualModelInfo>> ListVirtualModelsAsync();
    Task<List<ModelCandidateInfo>> GetModelCandidatesAsync(string virtualModelId);
    Task<ChatCompletionResult> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct);
    IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken ct);
    Task<EmbeddingResult> CreateEmbeddingsAsync(EmbeddingRequest request, CancellationToken ct);
    Task<FunctionCallResult> CallFunctionAsync(FunctionCallRequest request, CancellationToken ct);
}
