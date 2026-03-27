using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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
    public ObservableCollection<ChatUiAttachmentItem> AttachmentItems { get; } = [];

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
        set
        {
            if (!SetProperty(ref _attachments, value))
                return;

            RebuildAttachmentItems();
        }
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

    public bool IsUserMessage => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public string DisplayRole => IsUserMessage ? "You" : string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "FireBox" : Role;
    public HorizontalAlignment BubbleHorizontalAlignment => IsUserMessage ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public Thickness BubbleOuterMargin => IsUserMessage ? new Thickness(72, 2, 12, 2) : new Thickness(0, 2, 72, 2);
    public CornerRadius BubbleCornerRadius => IsUserMessage ? new CornerRadius(10, 10, 4, 10) : new CornerRadius(10, 10, 10, 4);
    public Brush BubbleBackground => IsUserMessage
        ? ResolveBrush("AccentFillColorSecondaryBrush", "AccentFillColorDefaultBrush")
        : ResolveBrush("CardBackgroundFillColorDefaultBrush", "LayerFillColorDefaultBrush");
    public Brush MessageForeground => IsUserMessage
        ? ResolveBrush("TextOnAccentFillColorPrimaryBrush", "TextFillColorPrimaryBrush")
        : ResolveBrush("TextFillColorPrimaryBrush", "TextFillColorPrimaryBrush");
    public Brush RoleForeground => IsUserMessage
        ? ResolveBrush("TextOnAccentFillColorSecondaryBrush", "TextFillColorSecondaryBrush")
        : ResolveBrush("TextFillColorSecondaryBrush", "TextFillColorSecondaryBrush");
    public bool HasAttachments => AttachmentItems.Count > 0;
    public Visibility AttachmentVisibility => HasAttachments ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PlainTextVisibility => ShouldRenderMarkdown ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MarkdownVisibility => ShouldRenderMarkdown ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyVisualStateChanged()
    {
        OnPropertyChanged(nameof(IsUserMessage));
        OnPropertyChanged(nameof(DisplayRole));
        OnPropertyChanged(nameof(BubbleHorizontalAlignment));
        OnPropertyChanged(nameof(BubbleOuterMargin));
        OnPropertyChanged(nameof(BubbleCornerRadius));
        OnPropertyChanged(nameof(BubbleBackground));
        OnPropertyChanged(nameof(MessageForeground));
        OnPropertyChanged(nameof(RoleForeground));
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(AttachmentVisibility));
        OnPropertyChanged(nameof(ShouldRenderMarkdown));
        OnPropertyChanged(nameof(PlainTextVisibility));
        OnPropertyChanged(nameof(MarkdownVisibility));
    }

    private void RebuildAttachmentItems()
    {
        AttachmentItems.Clear();

        if (_attachments is null || _attachments.Count == 0)
        {
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(AttachmentVisibility));
            return;
        }

        foreach (var attachment in _attachments)
        {
            var item = new ChatUiAttachmentItem(attachment);
            AttachmentItems.Add(item);
            _ = item.LoadPreviewAsync();
        }

        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(AttachmentVisibility));
    }

    private static Brush ResolveBrush(string resourceKey, string fallbackKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush)
            return brush;

        if (Application.Current.Resources.TryGetValue(fallbackKey, out value) && value is Brush fallbackBrush)
            return fallbackBrush;

        return new SolidColorBrush(Colors.Transparent);
    }
}
