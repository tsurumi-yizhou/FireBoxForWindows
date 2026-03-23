namespace Core.Models;

public sealed record EmbeddingResponse(
    string VirtualModelId,
    List<Embedding> Embeddings,
    ProviderSelection Selection,
    Usage Usage);
