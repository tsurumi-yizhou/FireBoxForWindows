using System;
using System.Collections.Generic;
using Demo.Models;

namespace Demo.Models;

public sealed class Conversation
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Chat";
    public List<ChatUiMessage> Messages { get; } = [];
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
}
