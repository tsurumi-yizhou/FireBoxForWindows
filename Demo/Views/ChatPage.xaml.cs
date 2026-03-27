using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using Core.Models;
using Demo.Models;
using Demo.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Demo.Views;

public sealed partial class ChatPage : UserControl
{
    public ChatViewModel ViewModel { get; }
    private readonly ResourceLoader _resourceLoader;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _modelsRefreshTimer;
    private int _feedbackVersion;
    private bool _didInitialLoad;
    private readonly List<PendingAttachment> _pendingAttachments = [];
    private long? _messageContextMenuTargetId;
    private string? _messageContextMenuSelectedText;
    private string? _messageContextMenuMessageContent;
    private TextBlock? _messageContextMenuSelectionTextBlock;

    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> s_videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".webm", ".mkv"
    };

    private static readonly HashSet<string> s_audioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
    };

    public ChatPage()
    {
        ViewModel = new ChatViewModel(
            App.Client,
            App.ConversationStore,
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        InitializeComponent();
        _resourceLoader = ResourceLoader.GetForViewIndependentUse();
        ApplyLocalizedStrings();
        ViewModel.SetAutoTitleFunctionDescription(GetRequiredString("ChatAutoTitleFunctionDescription"));
        _modelsRefreshTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _modelsRefreshTimer.Interval = TimeSpan.FromSeconds(20);
        _modelsRefreshTimer.IsRepeating = true;
        _modelsRefreshTimer.Tick += ModelsRefreshTimer_Tick;
        ModelSelector.ItemsSource = ViewModel.AvailableModels;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_didInitialLoad)
            return;

        _didInitialLoad = true;
        ViewModel.SetClient(App.Client);
        UpdateFeedbackBar();
        UpdateAttachmentComposer();
        ReloadModels();
        StartAutoRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoRefresh();
    }

    public void UpdateClient(Client.FireBoxClient? client)
    {
        ViewModel.SetClient(client);
        if (client is null)
        {
            ClearPendingAttachments();
            StopAutoRefresh();
            return;
        }

        ReloadModels();
        StartAutoRefresh();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatViewModel.FeedbackMessage) or nameof(ChatViewModel.FeedbackSeverity))
            UpdateFeedbackBar();
    }

    private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelSelector.SelectedItem is string modelId)
            ViewModel.SelectedModelId = modelId;
        else
            ViewModel.SelectedModelId = string.Empty;

        UpdateAttachmentComposer();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsStreaming)
        {
            ViewModel.TryCancelStreaming();
            return;
        }

        await SendAsync();
    }

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.CanSend)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (!ViewModel.CanSend)
            return;

        var attachments = await BuildAttachmentsAsync();
        if (attachments is null)
            return;

        await ViewModel.SendWithAttachmentsAsync(attachments);
        ClearPendingAttachments();
        ScrollToBottom();
    }

    public void ReloadModels()
    {
        if (!ViewModel.IsConnected || ViewModel.IsStreaming)
            return;

        ViewModel.LoadModels();

        if (!string.IsNullOrWhiteSpace(ViewModel.SelectedModelId) &&
            ViewModel.AvailableModels.Contains(ViewModel.SelectedModelId))
        {
            ModelSelector.SelectedItem = ViewModel.SelectedModelId;
            UpdateAttachmentComposer();
            return;
        }

        if (ViewModel.AvailableModels.Count > 0)
        {
            ModelSelector.SelectedIndex = 0;
            UpdateAttachmentComposer();
            return;
        }

        ModelSelector.SelectedIndex = -1;
        UpdateAttachmentComposer();
    }

    private void StartAutoRefresh()
    {
        if (!_modelsRefreshTimer.IsRunning)
            _modelsRefreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        if (_modelsRefreshTimer.IsRunning)
            _modelsRefreshTimer.Stop();
    }

    private void ModelsRefreshTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        ReloadModels();
    }

    private async void PickFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.SupportsAnyAttachmentInput)
            return;

        if (App.MainWindowInstance is null)
        {
            ViewModel.ReportFeedback(InfoBarSeverity.Error, "Cannot open file picker because the main window handle is unavailable.");
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
        };

        AddPickerFilters(picker);
        if (picker.FileTypeFilter.Count == 0)
            return;

        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        InitializeWithWindow.Initialize(picker, hwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
            return;

        var addedCount = 0;
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Name);
            var mediaFormat = ResolveMediaFormat(extension);
            if (mediaFormat is null)
                continue;

            if (!ViewModel.SupportsInputFormat(mediaFormat.Value))
                continue;

            if (_pendingAttachments.Any(item => string.Equals(item.FilePath, file.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var properties = await file.GetBasicPropertiesAsync();

            _pendingAttachments.Add(new PendingAttachment(
                mediaFormat.Value,
                string.IsNullOrWhiteSpace(file.ContentType) ? GuessMimeType(mediaFormat.Value, extension) : file.ContentType,
                file.Name,
                file.Path,
                unchecked((long)properties.Size)));
            addedCount++;
        }

        if (addedCount > 0)
        {
            RenderPendingAttachments();
        }
    }

    private async Task<List<ChatAttachment>?> BuildAttachmentsAsync()
    {
        if (_pendingAttachments.Count == 0)
            return [];

        var attachments = new List<ChatAttachment>(_pendingAttachments.Count);
        foreach (var item in _pendingAttachments)
        {
            if (!ViewModel.SupportsInputFormat(item.MediaFormat))
            {
                ViewModel.ReportFeedback(InfoBarSeverity.Warning, $"Current model does not support {item.MediaFormat} input. Remove unsupported files and retry.");
                return null;
            }

            try
            {
                var data = await File.ReadAllBytesAsync(item.FilePath);
                attachments.Add(new ChatAttachment(item.MediaFormat, item.MimeType, item.FileName, data, item.SizeBytes));
            }
            catch (Exception ex)
            {
                ViewModel.ReportFeedback(InfoBarSeverity.Error, $"Failed to read '{item.FileName}': {ex.Message}");
                return null;
            }
        }

        return attachments;
    }

    private void RemovePendingAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filePath)
            return;

        _pendingAttachments.RemoveAll(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        RenderPendingAttachments();
    }

    private void UpdateAttachmentComposer()
    {
        if (!ViewModel.SupportsAnyAttachmentInput)
        {
            AttachmentRail.Visibility = Visibility.Collapsed;
            PickFileButton.Visibility = Visibility.Collapsed;
            InputBox.PlaceholderText = "Type a message...";
            ClearPendingAttachments();
            return;
        }

        PickFileButton.Visibility = Visibility.Visible;
        InputBox.PlaceholderText = "Type a message or attach files...";

        _pendingAttachments.RemoveAll(item => !ViewModel.SupportsInputFormat(item.MediaFormat));
        RenderPendingAttachments();
    }

    private void RenderPendingAttachments()
    {
        AttachmentListPanel.Children.Clear();
        ViewModel.SetPendingAttachmentsCount(_pendingAttachments.Count);

        if (_pendingAttachments.Count == 0)
        {
            AttachmentRail.Visibility = Visibility.Collapsed;
            return;
        }

        AttachmentRail.Visibility = Visibility.Visible;

        foreach (var item in _pendingAttachments)
        {
            var rowSurface = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 4, 4),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            };
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "\uE8A5",
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var label = new TextBlock
            {
                Text = item.FileName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420,
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var removeButton = new Button
            {
                Content = "X",
                Tag = item.FilePath,
                Padding = new Thickness(6, 0, 6, 0),
                MinWidth = 24,
                Height = 24,
            };
            removeButton.Click += RemovePendingAttachment_Click;
            Grid.SetColumn(removeButton, 2);
            row.Children.Add(removeButton);

            rowSurface.Child = row;
            AttachmentListPanel.Children.Add(rowSurface);
        }
    }

    private void ClearPendingAttachments()
    {
        _pendingAttachments.Clear();
        RenderPendingAttachments();
    }

    private void AddPickerFilters(FileOpenPicker picker)
    {
        if (ViewModel.SupportsImageInput)
        {
            foreach (var ext in s_imageExtensions)
                picker.FileTypeFilter.Add(ext);
        }

        if (ViewModel.SupportsVideoInput)
        {
            foreach (var ext in s_videoExtensions)
                picker.FileTypeFilter.Add(ext);
        }

        if (ViewModel.SupportsAudioInput)
        {
            foreach (var ext in s_audioExtensions)
                picker.FileTypeFilter.Add(ext);
        }
    }

    private static MediaFormat? ResolveMediaFormat(string extension)
    {
        if (s_imageExtensions.Contains(extension)) return MediaFormat.Image;
        if (s_videoExtensions.Contains(extension)) return MediaFormat.Video;
        if (s_audioExtensions.Contains(extension)) return MediaFormat.Audio;
        return null;
    }

    private static string GuessMimeType(MediaFormat format, string extension)
    {
        if (format == MediaFormat.Image)
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        if (format == MediaFormat.Video)
            return "video/mp4";
        if (format == MediaFormat.Audio)
            return "audio/mpeg";
        return "application/octet-stream";
    }

    private void ScrollToBottom()
    {
        if (ViewModel.Messages.Count > 0)
        {
            MessageList.ScrollIntoView(ViewModel.Messages[^1]);
        }
    }

    private void FeedbackBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.FeedbackMessage))
            ViewModel.FeedbackMessage = null;
    }

    private async void MarkdownMessage_LinkClicked(object sender, LinkClickedEventArgs e)
    {
        if (!Uri.TryCreate(e.Link, UriKind.Absolute, out var uri))
            return;

        await Launcher.LaunchUriAsync(uri);
    }

    private void MessageBubble_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var menuAnchor = sender as FrameworkElement ?? FindMessageAnchor(e.OriginalSource as DependencyObject);
        if (menuAnchor is null || menuAnchor.DataContext is not ChatUiMessage message)
            return;

        _messageContextMenuTargetId = message.Id;
        _messageContextMenuSelectionTextBlock = FindSelectedTextBlock(e.OriginalSource as DependencyObject, menuAnchor);
        _messageContextMenuSelectedText = _messageContextMenuSelectionTextBlock?.SelectedText;
        _messageContextMenuMessageContent = message.Content;

        var hasSelectedText = !string.IsNullOrWhiteSpace(_messageContextMenuSelectedText);
        var hasMessageContent = !string.IsNullOrWhiteSpace(_messageContextMenuMessageContent);
        var supportsMessageActions = message.Role is "user" or "assistant";

        MessageBubbleSelectAllMenuItem.Visibility = Visibility.Visible;
        MessageBubbleCopyMenuItem.Visibility = hasSelectedText ? Visibility.Visible : Visibility.Collapsed;
        MessageBubbleRetryMenuItem.Visibility = !hasSelectedText && supportsMessageActions ? Visibility.Visible : Visibility.Collapsed;
        MessageBubbleDeleteMenuItem.Visibility = !hasSelectedText && supportsMessageActions ? Visibility.Visible : Visibility.Collapsed;

        MessageBubbleSelectAllMenuItem.IsEnabled = hasMessageContent;
        MessageBubbleCopyMenuItem.IsEnabled = hasSelectedText;
        MessageBubbleRetryMenuItem.IsEnabled = !hasSelectedText && supportsMessageActions && ViewModel.IsConnected && !ViewModel.IsStreaming;
        MessageBubbleDeleteMenuItem.IsEnabled = !hasSelectedText && supportsMessageActions && !ViewModel.IsStreaming;

        Windows.Foundation.Point position;
        if (!e.TryGetPosition(menuAnchor, out position))
            position = new Windows.Foundation.Point(menuAnchor.ActualWidth / 2, menuAnchor.ActualHeight / 2);

        MessageBubbleMenuFlyout.ShowAt(menuAnchor, new FlyoutShowOptions { Position = position });
        e.Handled = true;
    }

    private void MessageBubbleSelectAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_messageContextMenuSelectionTextBlock is not null)
        {
            _messageContextMenuSelectionTextBlock.SelectAll();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_messageContextMenuMessageContent))
            CopyTextToClipboard(_messageContextMenuMessageContent);
    }

    private void MessageBubbleCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_messageContextMenuSelectedText))
            return;

        CopyTextToClipboard(_messageContextMenuSelectedText);
    }

    private async void MessageBubbleRetryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_messageContextMenuTargetId is null)
            return;

        var messageId = _messageContextMenuTargetId.Value;
        ClearMessageContextMenuState();
        if (await ViewModel.RetryMessageAsync(messageId))
            ScrollToBottom();
    }

    private void MessageBubbleDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_messageContextMenuTargetId is null)
            return;

        var messageId = _messageContextMenuTargetId.Value;
        ClearMessageContextMenuState();
        ViewModel.DeleteMessage(messageId);
    }

    private void MessageBubbleMenuFlyout_Closed(object sender, object e)
    {
        ClearMessageContextMenuState();
    }

    private void ClearMessageContextMenuState()
    {
        _messageContextMenuTargetId = null;
        _messageContextMenuSelectedText = null;
        _messageContextMenuMessageContent = null;
        _messageContextMenuSelectionTextBlock = null;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T target)
                return target;

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static TextBlock? FindSelectedTextBlock(DependencyObject? originalSource, DependencyObject scopeRoot)
    {
        var directTextBlock = FindAncestorOrSelf<TextBlock>(originalSource);
        if (!string.IsNullOrWhiteSpace(directTextBlock?.SelectedText))
            return directTextBlock;

        return EnumerateDescendants(scopeRoot)
            .OfType<TextBlock>()
            .FirstOrDefault(static textBlock => !string.IsNullOrWhiteSpace(textBlock.SelectedText));
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;

            var childrenCount = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < childrenCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    private static FrameworkElement? FindMessageAnchor(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement element && element.DataContext is ChatUiMessage)
                return element;

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void UpdateFeedbackBar()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.FeedbackMessage) ||
            ViewModel.FeedbackSeverity is not (InfoBarSeverity.Warning or InfoBarSeverity.Error))
        {
            _feedbackVersion++;
            FeedbackBar.IsOpen = false;
            FeedbackBar.Title = string.Empty;
            FeedbackBar.Message = string.Empty;
            return;
        }

        var version = ++_feedbackVersion;
        FeedbackBar.Severity = ViewModel.FeedbackSeverity;
        FeedbackBar.Title = ViewModel.FeedbackSeverity switch
        {
            InfoBarSeverity.Success => "Success",
            InfoBarSeverity.Warning => "Notice",
            InfoBarSeverity.Error => "Request failed",
            _ => "Info",
        };
        FeedbackBar.Message = ViewModel.FeedbackMessage;
        FeedbackBar.IsOpen = true;
    }

    private void ApplyLocalizedStrings()
    {
        MessageBubbleSelectAllMenuItem.Text = GetRequiredString("ChatMessageMenuSelectAll");
        MessageBubbleCopyMenuItem.Text = GetRequiredString("ChatMessageMenuCopy");
        MessageBubbleRetryMenuItem.Text = GetRequiredString("ChatMessageMenuRetry");
        MessageBubbleDeleteMenuItem.Text = GetRequiredString("ChatMessageMenuDelete");
    }

    private string GetRequiredString(string key)
    {
        var value = _resourceLoader.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required string resource '{key}'.");

        return value;
    }

    private static void CopyTextToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }

    private sealed record PendingAttachment(
        MediaFormat MediaFormat,
        string MimeType,
        string FileName,
        string FilePath,
        long SizeBytes);
}
