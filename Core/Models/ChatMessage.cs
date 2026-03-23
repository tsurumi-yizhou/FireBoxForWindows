namespace Core.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    List<ChatAttachment>? Attachments = null);
