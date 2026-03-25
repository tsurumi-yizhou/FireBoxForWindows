namespace Core.Models;

public sealed record EmbeddingRequest(
    string ModelId,
    List<string> Input);
