namespace Core.Models;

public sealed record EmbeddingRequest(
    string VirtualModelId,
    List<string> Input);
