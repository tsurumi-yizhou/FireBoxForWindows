namespace Core.Models;

public sealed record EmbeddingResponse(
    string ModelId,
    List<Embedding> Embeddings,
    Usage Usage);
