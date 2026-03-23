namespace Core.Models;

public sealed record ChatCompletionResponse(
    string VirtualModelId,
    ChatMessage Message,
    string? ReasoningText,
    ProviderSelection Selection,
    Usage Usage,
    string FinishReason);
