using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using Core.Models;
using Demo.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Demo.Views;

public sealed partial class ChatPage : UserControl
{
    public ChatViewModel ViewModel { get; }
    private int _feedbackVersion;
    private bool _didInitialLoad;
    private readonly List<PendingAttachment> _pendingAttachments = [];

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
    }

    public void UpdateClient(Client.FireBoxClient? client)
    {
        ViewModel.SetClient(client);
        if (client is null)
            ClearPendingAttachments();
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

    private void ModelSelector_DropDownOpened(object sender, object e)
    {
        ReloadModels();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
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

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearChatCommand.Execute(null);
        ClearPendingAttachments();
    }

    private void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        ReloadModels();
    }

    public void ReloadModels()
    {
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
            AttachmentPanel.Visibility = Visibility.Collapsed;
            InputBox.PlaceholderText = "Type a message...";
            ClearPendingAttachments();
            return;
        }

        AttachmentPanel.Visibility = Visibility.Visible;
        InputBox.PlaceholderText = "Type a message or attach files...";
        AttachmentHintText.Text = $"Supported input: {ViewModel.SelectedModelInputFormatsLabel}";

        _pendingAttachments.RemoveAll(item => !ViewModel.SupportsInputFormat(item.MediaFormat));
        RenderPendingAttachments();
    }

    private void RenderPendingAttachments()
    {
        AttachmentListPanel.Children.Clear();
        ViewModel.SetPendingAttachmentsCount(_pendingAttachments.Count);

        if (_pendingAttachments.Count == 0)
            return;

        foreach (var item in _pendingAttachments)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = $"{item.FileName} ({item.MediaFormat}, {FormatFileSize(item.SizeBytes)})",
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var removeButton = new Button
            {
                Content = "Remove",
                Tag = item.FilePath,
            };
            removeButton.Click += RemovePendingAttachment_Click;
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            AttachmentListPanel.Children.Add(row);
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

    private static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes >= 1024L * 1024L)
            return $"{sizeBytes / (1024d * 1024d):F1} MB";
        if (sizeBytes >= 1024L)
            return $"{sizeBytes / 1024d:F1} KB";
        return $"{sizeBytes} B";
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

    private sealed record PendingAttachment(
        MediaFormat MediaFormat,
        string MimeType,
        string FileName,
        string FilePath,
        long SizeBytes);
}
