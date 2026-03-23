namespace Core.Models;

public sealed record FunctionCallRequest(
    string VirtualModelId,
    string FunctionName,
    string FunctionDescription,
    string InputJson,
    string InputSchemaJson,
    string OutputSchemaJson,
    float Temperature = 0f,
    int MaxOutputTokens = -1);
