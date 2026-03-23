namespace Demo.Models;

public sealed class ChatUiMessage
{
    public long Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ReasoningContent { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsStreaming { get; set; }
}
