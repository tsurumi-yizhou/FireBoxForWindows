using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Demo.Models;

public sealed partial class Conversation : ObservableObject
{
    private string _title = "New Chat";
    private DateTimeOffset _updatedAt = DateTimeOffset.Now;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string SessionFileStem { get; init; } = CreateSessionFileStem(DateTimeOffset.UtcNow);

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public List<ChatUiMessage> Messages { get; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public static string CreateSessionFileStem(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'");
}
