namespace Core.Models;

public sealed record ChatCompletionRequest(
    string VirtualModelId,
    List<ChatMessage> Messages,
    List<ChatAttachment>? Attachments = null,
    float Temperature = -1f,
    int MaxOutputTokens = -1,
    FireBoxReasoningEffort ReasoningEffort = FireBoxReasoningEffort.Default);
