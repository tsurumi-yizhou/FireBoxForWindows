using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Models;
using Demo.Models;
using Demo.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Demo.ViewModels;

#pragma warning disable MVVMTK0045

public partial class ChatViewModel : ObservableObject
{
    private Client.FireBoxClient? _client;
    private readonly ConversationJsonlStore _conversationStore;
    private readonly DispatcherQueue _dispatcherQueue;
    private long _messageIdCounter;
    private CancellationTokenSource? _streamCts;
    private readonly Dictionary<string, VirtualModelInfo> _modelsById = new(StringComparer.OrdinalIgnoreCase);
    private Conversation? _activeConversation;

    public ObservableCollection<Conversation> Conversations { get; } = [];

    public ObservableCollection<ChatUiMessage> Messages { get; } = [];

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _selectedModelId = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _feedbackMessage;

    [ObservableProperty]
    private InfoBarSeverity _feedbackSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = [];

    [ObservableProperty]
    private bool _hasPendingAttachments;

    public bool IsConnected => _client is not null;

    public bool CanSend =>
        (!string.IsNullOrWhiteSpace(InputText) || HasPendingAttachments)
        && !string.IsNullOrWhiteSpace(SelectedModelId)
        && !IsStreaming
        && IsConnected;

    public bool SupportsImageInput => SupportsInputFormat(ModelMediaFormat.Image);
    public bool SupportsVideoInput => SupportsInputFormat(ModelMediaFormat.Video);
    public bool SupportsAudioInput => SupportsInputFormat(ModelMediaFormat.Audio);
    public bool SupportsAnyAttachmentInput => SupportsImageInput || SupportsVideoInput || SupportsAudioInput;

    public string SelectedModelInputFormatsLabel
    {
        get
        {
            var formats = GetSelectedInputFormats();
            if (formats.Count == 0)
                return "Text only";

            return string.Join(", ", formats.Select(static format => format switch
            {
                ModelMediaFormat.Image => "Image",
                ModelMediaFormat.Video => "Video",
                ModelMediaFormat.Audio => "Audio",
                _ => format.ToString(),
            }));
        }
    }

    public ChatViewModel(
        Client.FireBoxClient? client,
        ConversationJsonlStore conversationStore,
        DispatcherQueue dispatcherQueue)
    {
        _client = client;
        _conversationStore = conversationStore;
        _dispatcherQueue = dispatcherQueue;
        LoadPersistedConversations();
    }

    public void SetClient(Client.FireBoxClient? client)
    {
        _client = client;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(CanSend));

    partial void OnSelectedModelIdChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(SupportsImageInput));
        OnPropertyChanged(nameof(SupportsVideoInput));
        OnPropertyChanged(nameof(SupportsAudioInput));
        OnPropertyChanged(nameof(SupportsAnyAttachmentInput));
        OnPropertyChanged(nameof(SelectedModelInputFormatsLabel));
    }

    partial void OnIsStreamingChanged(bool value) => OnPropertyChanged(nameof(CanSend));
    partial void OnHasPendingAttachmentsChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    public void LoadModels()
    {
        if (_client is null)
        {
            _modelsById.Clear();
            SelectedModelId = string.Empty;
            Error = "Service not connected.";
            SetFeedback(InfoBarSeverity.Warning, Error);
            return;
        }

        try
        {
            var previousSelection = SelectedModelId;
            var models = _client.ListModels();
            _modelsById.Clear();
            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model.VirtualModelId);
                _modelsById[model.VirtualModelId] = model;
            }

            if (AvailableModels.Count == 0)
            {
                SelectedModelId = string.Empty;
                Error = "No virtual models available. Configure at least one Route in App (Routes page), then ensure Demo is allowed and refresh.";
                SetFeedback(InfoBarSeverity.Warning, Error);
                return;
            }

            Error = null;
            SetFeedback(InfoBarSeverity.Success, $"Loaded {AvailableModels.Count} virtual model{(AvailableModels.Count == 1 ? string.Empty : "s")}.");
            if (!string.IsNullOrWhiteSpace(previousSelection) && AvailableModels.Contains(previousSelection))
                SelectedModelId = previousSelection;
            else
                SelectedModelId = AvailableModels[0];
        }
        catch (Exception ex)
        {
            _modelsById.Clear();
            SelectedModelId = string.Empty;
            Error = BuildFriendlyError(ex, "Load models");
            SetFeedback(InfoBarSeverity.Error, Error);
        }
    }

    public void SetPendingAttachmentsCount(int count)
    {
        HasPendingAttachments = count > 0;
    }

    public void ReportFeedback(InfoBarSeverity severity, string? message)
    {
        SetFeedback(severity, message);
    }

    public bool SupportsInputFormat(ModelMediaFormat format)
    {
        if (!_modelsById.TryGetValue(SelectedModelId, out var model))
            return false;

        return model.Capabilities.InputFormats?.Contains(format) == true;
    }

    public IReadOnlyList<ModelMediaFormat> GetSelectedInputFormats()
    {
        if (!_modelsById.TryGetValue(SelectedModelId, out var model))
            return [];

        return model.Capabilities.InputFormats ?? [];
    }

    public Conversation AddConversation(Conversation conv)
    {
        TouchConversation(conv, persist: false);
        Conversations.Insert(0, conv);
        _conversationStore.SaveConversation(conv);
        return conv;
    }

    public void SwitchConversation(string id)
    {
        if (_activeConversation is not null)
        {
            _activeConversation.Messages.Clear();
            _activeConversation.Messages.AddRange(Messages);
        }

        _activeConversation = Conversations.FirstOrDefault(c => c.Id == id);
        Messages.Clear();
        if (_activeConversation is not null)
        {
            foreach (var msg in _activeConversation.Messages)
                Messages.Add(msg);
        }
        Error = null;
    }

    public void DeleteConversation(string id)
    {
        var conv = Conversations.FirstOrDefault(c => c.Id == id);
        if (conv is null) return;
        Conversations.Remove(conv);
        _conversationStore.DeleteConversation(conv);
        if (_activeConversation?.Id == id)
        {
            _activeConversation = null;
            Messages.Clear();
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        await SendWithAttachmentsAsync(null);
    }

    public async Task SendWithAttachmentsAsync(List<ChatAttachment>? attachments)
    {
        if (!CanSend || _client is null) return;

        Error = null;
        FeedbackMessage = null;
        List<ChatAttachment>? inputAttachments = attachments is { Count: > 0 } ? attachments.ToList() : null;
        var userText = InputText.Trim();
        if (string.IsNullOrWhiteSpace(userText) && inputAttachments is { Count: > 0 })
            userText = $"[Attached {inputAttachments.Count} file{(inputAttachments.Count == 1 ? string.Empty : "s")}]";
        InputText = string.Empty;

        var userMsg = new ChatUiMessage
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "user",
            Content = userText,
            Attachments = inputAttachments,
        };
        Messages.Add(userMsg);
        PersistActiveConversation();

        if (_activeConversation is not null && _activeConversation.Title == "New Chat")
        {
            _activeConversation.Title = userText.Length > 30 ? userText[..30] + "..." : userText;
            PersistActiveConversation();
        }

        var assistantMsg = new ChatUiMessage
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "assistant",
            IsStreaming = true,
        };
        Messages.Add(assistantMsg);

        IsStreaming = true;
        _streamCts = new CancellationTokenSource();
        var request = new ChatCompletionRequest(
            SelectedModelId,
            Messages
                .Where(static m => (m.Role is "user" or "assistant" or "system")
                    && (!string.IsNullOrEmpty(m.Content) || m.Attachments is { Count: > 0 }))
                .Select(m => new ChatMessage(m.Role, m.Content, m.Attachments))
                .ToList());

        try
        {
            await foreach (var evt in _client.ChatCompletionStreamAsync(request, _streamCts.Token))
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    switch (evt.Type)
                    {
                        case ChatStreamEventType.Delta:
                            assistantMsg.Content += evt.DeltaText;
                            break;
                        case ChatStreamEventType.ReasoningDelta:
                            assistantMsg.ReasoningContent = (assistantMsg.ReasoningContent ?? "") + evt.ReasoningText;
                            break;
                        case ChatStreamEventType.Completed:
                            assistantMsg.IsStreaming = false;
                            if (evt.Response is not null)
                                assistantMsg.Content = evt.Response.Message.Content;
                            SetFeedback(InfoBarSeverity.Success, "Response completed.");
                            PersistActiveConversation();
                            break;
                        case ChatStreamEventType.Error:
                            var streamError = evt.Error?.Message ?? "Unknown error";
                            assistantMsg.ErrorMessage = streamError;
                            assistantMsg.Content = $"[Request failed] {streamError}";
                            Error = streamError;
                            SetFeedback(InfoBarSeverity.Error, streamError);
                            assistantMsg.IsStreaming = false;
                            PersistActiveConversation();
                            break;
                        case ChatStreamEventType.Cancelled:
                            Error = "Request cancelled.";
                            SetFeedback(InfoBarSeverity.Warning, "Request cancelled.");
                            assistantMsg.IsStreaming = false;
                            PersistActiveConversation();
                            break;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                Error = "Request cancelled.";
                SetFeedback(InfoBarSeverity.Warning, "Request cancelled.");
                assistantMsg.IsStreaming = false;
                PersistActiveConversation();
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var requestError = BuildFriendlyError(ex, "Request");
                assistantMsg.ErrorMessage = requestError;
                assistantMsg.Content = $"[Request failed] {requestError}";
                Error = requestError;
                SetFeedback(InfoBarSeverity.Error, requestError);
                assistantMsg.IsStreaming = false;
                PersistActiveConversation();
            });
        }
        finally
        {
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _streamCts?.Cancel();
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        Error = null;
        SetFeedback(InfoBarSeverity.Informational, "Chat cleared.");
        if (_activeConversation is not null)
        {
            _activeConversation.Messages.Clear();
            TouchConversation(_activeConversation);
        }
    }

    private void LoadPersistedConversations()
    {
        foreach (var conversation in _conversationStore.LoadAll())
        {
            Conversations.Add(conversation);
            UpdateMessageIdCounter(conversation);
        }
    }

    private void PersistActiveConversation()
    {
        if (_activeConversation is null)
            return;

        _activeConversation.Messages.Clear();
        _activeConversation.Messages.AddRange(Messages);
        TouchConversation(_activeConversation);
    }

    private void TouchConversation(Conversation conversation, bool persist = true)
    {
        conversation.UpdatedAt = DateTimeOffset.Now;
        UpdateMessageIdCounter(conversation);
        MoveConversationToTop(conversation);

        if (persist)
            _conversationStore.SaveConversation(conversation);
    }

    private void MoveConversationToTop(Conversation conversation)
    {
        var index = Conversations.IndexOf(conversation);
        if (index <= 0)
            return;

        Conversations.Move(index, 0);
    }

    private void UpdateMessageIdCounter(Conversation conversation)
    {
        var maxMessageId = conversation.Messages.Count == 0 ? 0 : conversation.Messages.Max(static message => message.Id);
        if (maxMessageId > _messageIdCounter)
            _messageIdCounter = maxMessageId;
    }

    private static string BuildFriendlyError(Exception ex, string operation)
    {
        if (ex is UnauthorizedAccessException || ex.Message.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
            return $"{operation} failed: access denied by Service. Approve Demo in App Allowlist, then retry.";

        if (ex is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return $"{operation} timed out. Please retry.";

        if (ex is COMException comEx && (uint)comEx.HResult == 0x800706BA)
            return $"{operation} failed: Service is unreachable (RPC server unavailable).";

        return $"{operation} failed: {ex.Message}";
    }

    private void SetFeedback(InfoBarSeverity severity, string? message)
    {
        if (severity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
        {
            FeedbackSeverity = severity;
            FeedbackMessage = null;
            return;
        }

        FeedbackSeverity = severity;
        FeedbackMessage = message;
    }
}
