using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.Models;
using Microsoft.UI.Xaml;

namespace Demo.Models;

public sealed partial class ChatUiMessage : ObservableObject
{
    private string _role = string.Empty;
    private string _content = string.Empty;
    private List<ChatAttachment>? _attachments;
    private string? _reasoningContent;
    private string? _errorMessage;
    private bool _isStreaming;

    public long Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public string Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
                NotifyVisualStateChanged();
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
                NotifyVisualStateChanged();
        }
    }

    public List<ChatAttachment>? Attachments
    {
        get => _attachments;
        set => SetProperty(ref _attachments, value);
    }

    public string? ReasoningContent
    {
        get => _reasoningContent;
        set => SetProperty(ref _reasoningContent, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
                NotifyVisualStateChanged();
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (SetProperty(ref _isStreaming, value))
                NotifyVisualStateChanged();
        }
    }

    public bool ShouldRenderMarkdown =>
        !IsStreaming &&
        string.IsNullOrWhiteSpace(ErrorMessage) &&
        string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Content);

    public Visibility PlainTextVisibility => ShouldRenderMarkdown ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MarkdownVisibility => ShouldRenderMarkdown ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyVisualStateChanged()
    {
        OnPropertyChanged(nameof(ShouldRenderMarkdown));
        OnPropertyChanged(nameof(PlainTextVisibility));
        OnPropertyChanged(nameof(MarkdownVisibility));
    }
}
