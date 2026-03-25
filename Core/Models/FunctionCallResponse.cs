namespace Core.Models;

public sealed record FunctionCallResponse(
    string ModelId,
    string OutputJson,
    Usage Usage,
    string FinishReason);
