namespace Core.Models;

public enum ChatStreamEventType
{
    Started = 0,
    Delta = 1,
    Usage = 2,
    Completed = 3,
    Error = 4,
    Cancelled = 5,
    ReasoningDelta = 6,
}

public sealed record ChatStreamEvent(
    long RequestId,
    ChatStreamEventType Type,
    string? DeltaText = null,
    string? ReasoningText = null,
    ProviderSelection? Selection = null,
    Usage? Usage = null,
    ChatCompletionResponse? Response = null,
    FireBoxError? Error = null);
