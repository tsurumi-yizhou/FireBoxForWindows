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
    private readonly Dictionary<string, ModelInfo> _modelsById = new(StringComparer.OrdinalIgnoreCase);
    private Conversation? _activeConversation;
    private string _autoTitleFunctionDescription = string.Empty;

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

    [ObservableProperty]
    private ReasoningEffortOption? _selectedReasoningEffort;

    public bool IsConnected => _client is not null;

    public ObservableCollection<ReasoningEffortOption> ReasoningEffortOptions { get; } =
        new(ReasoningEfforts.SupportedValues.Select(static effort =>
            new ReasoningEffortOption(effort, ReasoningEfforts.ToDisplayName(effort))));

    public bool CanSend =>
        (!string.IsNullOrWhiteSpace(InputText) || HasPendingAttachments)
        && !string.IsNullOrWhiteSpace(SelectedModelId)
        && !IsStreaming
        && IsConnected;

    public bool CanSubmitOrAbort => IsStreaming || CanSend;
    public Symbol SendButtonSymbol => IsStreaming ? Symbol.Stop : Symbol.Send;
    public string ActiveConversationTitle => _activeConversation?.Title ?? string.Empty;

    public bool SupportsImageInput => SupportsInputFormat(MediaFormat.Image);
    public bool SupportsVideoInput => SupportsInputFormat(MediaFormat.Video);
    public bool SupportsAudioInput => SupportsInputFormat(MediaFormat.Audio);
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
                MediaFormat.Image => "Image",
                MediaFormat.Video => "Video",
                MediaFormat.Audio => "Audio",
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
        SelectedReasoningEffort = ReasoningEffortOptions.FirstOrDefault();
        LoadPersistedConversations();
    }

    public void SetClient(Client.FireBoxClient? client)
    {
        _client = client;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSubmitOrAbort));
    }

    public void SetAutoTitleFunctionDescription(string functionDescription)
    {
        if (string.IsNullOrWhiteSpace(functionDescription))
            throw new ArgumentException("Function description is required.", nameof(functionDescription));

        _autoTitleFunctionDescription = functionDescription;
    }

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSubmitOrAbort));
    }

    partial void OnSelectedModelIdChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSubmitOrAbort));
        OnPropertyChanged(nameof(SupportsImageInput));
        OnPropertyChanged(nameof(SupportsVideoInput));
        OnPropertyChanged(nameof(SupportsAudioInput));
        OnPropertyChanged(nameof(SupportsAnyAttachmentInput));
        OnPropertyChanged(nameof(SelectedModelInputFormatsLabel));
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSubmitOrAbort));
        OnPropertyChanged(nameof(SendButtonSymbol));
    }

    partial void OnHasPendingAttachmentsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSubmitOrAbort));
    }

    public bool TryCancelStreaming()
    {
        if (!IsStreaming || _streamCts is null)
            return false;

        _streamCts.Cancel();
        return true;
    }

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
                AvailableModels.Add(model.ModelId);
                _modelsById[model.ModelId] = model;
            }

            if (AvailableModels.Count == 0)
            {
                SelectedModelId = string.Empty;
                Error = "No models available. Configure at least one Route in App (Routes page), then ensure Demo is allowed and refresh.";
                SetFeedback(InfoBarSeverity.Warning, Error);
                return;
            }

            Error = null;
            SetFeedback(InfoBarSeverity.Success, $"Loaded {AvailableModels.Count} model{(AvailableModels.Count == 1 ? string.Empty : "s")}.");
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

    public bool SupportsInputFormat(MediaFormat format)
    {
        if (!_modelsById.TryGetValue(SelectedModelId, out var model))
            return false;

        return model.Capabilities.InputFormats?.Contains(format) == true;
    }

    public IReadOnlyList<MediaFormat> GetSelectedInputFormats()
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

        OnPropertyChanged(nameof(ActiveConversationTitle));
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
            OnPropertyChanged(nameof(ActiveConversationTitle));
        }
    }

    public bool DeleteMessage(long messageId)
    {
        if (IsStreaming)
            return false;

        var messageIndex = FindMessageIndexById(messageId);
        if (messageIndex < 0)
            return false;

        Messages.RemoveAt(messageIndex);
        PersistActiveConversation();
        return true;
    }

    public async Task<bool> RetryMessageAsync(long messageId)
    {
        if (IsStreaming || _client is null || string.IsNullOrWhiteSpace(SelectedModelId))
            return false;

        var targetIndex = FindMessageIndexById(messageId);
        if (targetIndex < 0)
            return false;

        var retryUserIndex = ResolveRetryUserMessageIndex(targetIndex);
        if (retryUserIndex < 0)
            return false;

        var assistantIndex = Messages[targetIndex].IsUserMessage
            ? FindNextAssistantMessageIndex(retryUserIndex)
            : targetIndex;

        ChatUiMessage assistantMessage;
        if (assistantIndex >= 0)
        {
            assistantMessage = Messages[assistantIndex];
            ResetAssistantMessageForRetry(assistantMessage);
        }
        else
        {
            assistantMessage = CreateStreamingAssistantMessage();
            var insertIndex = Math.Min(retryUserIndex + 1, Messages.Count);
            Messages.Insert(insertIndex, assistantMessage);
        }

        var requestMessages = Messages
            .Take(retryUserIndex + 1)
            .Where(IsRequestContextMessage)
            .Select(message => new ChatMessage(message.Role, message.Content, message.Attachments))
            .ToList();
        if (requestMessages.Count == 0)
            return false;

        PersistActiveConversation();
        await RunAssistantStreamingAsync(assistantMessage, requestMessages);
        return true;
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
            _activeConversation.Title = await GenerateConversationTitleAsync(userText);
            PersistActiveConversation();
            OnPropertyChanged(nameof(ActiveConversationTitle));
        }

        var assistantMsg = new ChatUiMessage
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "assistant",
            IsStreaming = true,
        };
        Messages.Add(assistantMsg);

        var requestMessages = Messages
            .Where(IsRequestContextMessage)
            .Select(message => new ChatMessage(message.Role, message.Content, message.Attachments))
            .ToList();
        await RunAssistantStreamingAsync(assistantMsg, requestMessages);
    }

    private async Task RunAssistantStreamingAsync(ChatUiMessage assistantMessage, List<ChatMessage> requestMessages)
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedModelId))
            return;

        IsStreaming = true;
        _streamCts = new CancellationTokenSource();
        var request = new ChatCompletionRequest(
            SelectedModelId,
            requestMessages,
            -1f,
            -1,
            SelectedReasoningEffort?.Value ?? ReasoningEffort.Default);

        try
        {
            await foreach (var evt in _client.ChatCompletionStreamAsync(request, _streamCts.Token))
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    switch (evt.Type)
                    {
                        case ChatStreamEventType.Delta:
                            assistantMessage.Content += evt.DeltaText;
                            break;
                        case ChatStreamEventType.ReasoningDelta:
                            assistantMessage.ReasoningContent = (assistantMessage.ReasoningContent ?? "") + evt.ReasoningText;
                            break;
                        case ChatStreamEventType.Completed:
                            assistantMessage.IsStreaming = false;
                            if (evt.Response is not null)
                                assistantMessage.Content = evt.Response.Message.Content;
                            SetFeedback(InfoBarSeverity.Success, "Response completed.");
                            PersistActiveConversation();
                            break;
                        case ChatStreamEventType.Error:
                            var streamError = evt.Error ?? "Unknown error";
                            assistantMessage.ErrorMessage = streamError;
                            assistantMessage.Content = $"[Request failed] {streamError}";
                            Error = streamError;
                            SetFeedback(InfoBarSeverity.Error, streamError);
                            assistantMessage.IsStreaming = false;
                            PersistActiveConversation();
                            break;
                        case ChatStreamEventType.Cancelled:
                            Error = "Request cancelled.";
                            SetFeedback(InfoBarSeverity.Warning, "Request cancelled.");
                            assistantMessage.IsStreaming = false;
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
                assistantMessage.IsStreaming = false;
                PersistActiveConversation();
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var requestError = BuildFriendlyError(ex, "Request");
                assistantMessage.ErrorMessage = requestError;
                assistantMessage.Content = $"[Request failed] {requestError}";
                Error = requestError;
                SetFeedback(InfoBarSeverity.Error, requestError);
                assistantMessage.IsStreaming = false;
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

    private int FindMessageIndexById(long messageId)
    {
        for (var index = 0; index < Messages.Count; index++)
        {
            if (Messages[index].Id == messageId)
                return index;
        }

        return -1;
    }

    private int ResolveRetryUserMessageIndex(int messageIndex)
    {
        if (messageIndex < 0 || messageIndex >= Messages.Count)
            return -1;

        var candidate = Messages[messageIndex];
        if (candidate.IsUserMessage)
            return messageIndex;

        for (var index = messageIndex - 1; index >= 0; index--)
        {
            if (Messages[index].IsUserMessage)
                return index;
        }

        return -1;
    }

    private int FindNextAssistantMessageIndex(int userMessageIndex)
    {
        for (var index = userMessageIndex + 1; index < Messages.Count; index++)
        {
            var message = Messages[index];
            if (message.IsUserMessage)
                break;

            if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private ChatUiMessage CreateStreamingAssistantMessage() =>
        new()
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "assistant",
            IsStreaming = true,
        };

    private static void ResetAssistantMessageForRetry(ChatUiMessage assistantMessage)
    {
        assistantMessage.Role = "assistant";
        assistantMessage.Content = string.Empty;
        assistantMessage.ReasoningContent = null;
        assistantMessage.ErrorMessage = null;
        assistantMessage.IsStreaming = true;
    }

    private static bool IsRequestContextMessage(ChatUiMessage message) =>
        (message.Role is "user" or "assistant" or "system")
        && (!string.IsNullOrEmpty(message.Content) || message.Attachments is { Count: > 0 });

    private async Task<string> GenerateConversationTitleAsync(string userText)
    {
        var fallbackTitle = BuildFallbackConversationTitle(userText);
        if (_client is null ||
            string.IsNullOrWhiteSpace(SelectedModelId) ||
            string.IsNullOrWhiteSpace(_autoTitleFunctionDescription))
        {
            return fallbackTitle;
        }

        var titleParameters = new AutoTitleFunctionParameters(
            Messages
                .Where(static message => message.Role is "user" or "assistant")
                .Select(static message => new AutoTitleMessage(message.Role, message.Content))
                .ToList());

        Result<string> titleResult;
        try
        {
            titleResult = await Task.Run(() =>
                _client.CallFunction<AutoTitleFunctionParameters, string>(
                    SelectedModelId,
                    _autoTitleFunctionDescription,
                    titleParameters));
        }
        catch
        {
            return fallbackTitle;
        }

        if (!titleResult.IsSuccess || titleResult.Response is null)
            return fallbackTitle;

        var normalized = NormalizeTitle(titleResult.Response);
        return string.IsNullOrWhiteSpace(normalized) ? fallbackTitle : normalized;
    }

    private static string BuildFallbackConversationTitle(string userText)
    {
        var trimmed = userText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return userText;

        return trimmed.Length > 30 ? trimmed[..30] + "..." : trimmed;
    }

    private static string NormalizeTitle(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return string.Empty;

        return rawTitle
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
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

    public sealed record ReasoningEffortOption(ReasoningEffort Value, string Label);
    private sealed record AutoTitleFunctionParameters(List<AutoTitleMessage> Messages);
    private sealed record AutoTitleMessage(string Role, string Content);
}
