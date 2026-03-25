namespace Core.Models;

public sealed record ChatCompletionRequest(
    string ModelId,
    List<ChatMessage> Messages,
    float Temperature = -1f,
    int MaxOutputTokens = -1,
    ReasoningEffort ReasoningEffort = ReasoningEffort.Default);
