namespace Core.Models;

public sealed record FunctionCallResponse(
    string VirtualModelId,
    string OutputJson,
    ProviderSelection Selection,
    Usage Usage,
    string FinishReason);
