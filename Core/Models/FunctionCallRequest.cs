namespace Core.Models;

public sealed record FunctionCallRequest(
    string ModelId,
    string FunctionName,
    string FunctionDescription,
    string InputJson,
    string InputSchemaJson,
    string OutputSchemaJson,
    float Temperature = 0f,
    int MaxOutputTokens = -1);
