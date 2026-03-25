namespace Core.Models;

public sealed record ChatCompletionResponse(
    string ModelId,
    ChatMessage Message,
    string? ReasoningText,
    Usage Usage,
    string FinishReason);
