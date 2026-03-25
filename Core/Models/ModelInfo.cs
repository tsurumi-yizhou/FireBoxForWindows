namespace Core.Models;

public sealed record ModelInfo(
    string ModelId,
    ModelCapabilities Capabilities,
    bool Available);
