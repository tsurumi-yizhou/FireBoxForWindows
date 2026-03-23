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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

namespace Demo.ViewModels;

#pragma warning disable MVVMTK0045

public partial class ChatViewModel : ObservableObject
{
    private Client.FireBoxClient? _client;
    private readonly DispatcherQueue _dispatcherQueue;
    private long _messageIdCounter;
    private CancellationTokenSource? _streamCts;

    // --- Multi-conversation ---
    private readonly List<Conversation> _conversations = [];
    private Conversation? _activeConversation;

    public IReadOnlyList<Conversation> Conversations => _conversations;

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

    public bool IsConnected => _client is not null;

    public bool CanSend =>
        !string.IsNullOrWhiteSpace(InputText)
        && !string.IsNullOrWhiteSpace(SelectedModelId)
        && !IsStreaming
        && IsConnected;

    public ChatViewModel(Client.FireBoxClient? client, DispatcherQueue dispatcherQueue)
    {
        _client = client;
        _dispatcherQueue = dispatcherQueue;
    }

    public void SetClient(Client.FireBoxClient? client)
    {
        _client = client;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(CanSend));
    partial void OnSelectedModelIdChanged(string value) => OnPropertyChanged(nameof(CanSend));
    partial void OnIsStreamingChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    public void LoadModels()
    {
        if (_client is null)
        {
            Error = "Service not connected.";
            SetFeedback(InfoBarSeverity.Warning, Error);
            return;
        }

        try
        {
            var previousSelection = SelectedModelId;
            var models = _client.ListModels();
            AvailableModels.Clear();
            foreach (var m in models)
                AvailableModels.Add(m.VirtualModelId);

            if (AvailableModels.Count == 0)
            {
                SelectedModelId = string.Empty;
                Error = "No virtual models available. Configure at least one Route in App (Routes page), then ensure Demo is allowed and refresh.";
                SetFeedback(InfoBarSeverity.Warning, Error);
                return;
            }

            Error = null;
            SetFeedback(InfoBarSeverity.Success, $"Loaded {AvailableModels.Count} virtual model{(AvailableModels.Count == 1 ? string.Empty : "s")}.");
            if (!string.IsNullOrWhiteSpace(previousSelection) &&
                AvailableModels.Contains(previousSelection))
            {
                SelectedModelId = previousSelection;
            }
            else
            {
                SelectedModelId = AvailableModels[0];
            }
        }
        catch (Exception ex)
        {
            SelectedModelId = string.Empty;
            Error = BuildFriendlyError(ex, "Load models");
            SetFeedback(InfoBarSeverity.Error, Error);
        }
    }

    // --- Conversation management ---

    public Conversation AddConversation(Conversation conv)
    {
        _conversations.Add(conv);
        return conv;
    }

    public void SwitchConversation(string id)
    {
        // Save current messages
        if (_activeConversation is not null)
        {
            _activeConversation.Messages.Clear();
            _activeConversation.Messages.AddRange(Messages);
        }

        _activeConversation = _conversations.FirstOrDefault(c => c.Id == id);
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
        var conv = _conversations.FirstOrDefault(c => c.Id == id);
        if (conv is null) return;
        _conversations.Remove(conv);
        if (_activeConversation?.Id == id)
        {
            _activeConversation = null;
            Messages.Clear();
        }
    }

    // --- Send ---

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!CanSend || _client is null) return;

        Error = null;
        FeedbackMessage = null;
        var userText = InputText.Trim();
        InputText = string.Empty;

        var userMsg = new ChatUiMessage
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "user",
            Content = userText,
        };
        Messages.Add(userMsg);

        // Auto-title conversation
        if (_activeConversation is not null && _activeConversation.Title == "New Chat")
            _activeConversation.Title = userText.Length > 30 ? userText[..30] + "..." : userText;

        var assistantMsg = new ChatUiMessage
        {
            Id = Interlocked.Increment(ref _messageIdCounter),
            Role = "assistant",
            IsStreaming = true,
        };
        Messages.Add(assistantMsg);

        IsStreaming = true;
        _streamCts = new CancellationTokenSource();
        var request = new ChatCompletionRequest(SelectedModelId, Messages
            .Where(m => m.Role is "user" or "assistant" or "system" && !string.IsNullOrEmpty(m.Content))
            .Select(m => new ChatMessage(m.Role, m.Content))
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
                            RefreshMessage(assistantMsg);
                            break;
                        case ChatStreamEventType.ReasoningDelta:
                            assistantMsg.ReasoningContent = (assistantMsg.ReasoningContent ?? "") + evt.ReasoningText;
                            break;
                        case ChatStreamEventType.Completed:
                            assistantMsg.IsStreaming = false;
                            if (evt.Response is not null)
                                assistantMsg.Content = evt.Response.Message.Content;
                            SetFeedback(InfoBarSeverity.Success, "Response completed.");
                            RefreshMessage(assistantMsg);
                            break;
                        case ChatStreamEventType.Error:
                            var streamError = evt.Error?.Message ?? "Unknown error";
                            assistantMsg.ErrorMessage = streamError;
                            assistantMsg.Content = $"[Request failed] {streamError}";
                            Error = streamError;
                            SetFeedback(InfoBarSeverity.Error, streamError);
                            assistantMsg.IsStreaming = false;
                            RefreshMessage(assistantMsg);
                            break;
                        case ChatStreamEventType.Cancelled:
                            Error = "Request cancelled.";
                            SetFeedback(InfoBarSeverity.Warning, "Request cancelled.");
                            assistantMsg.IsStreaming = false;
                            RefreshMessage(assistantMsg);
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
                RefreshMessage(assistantMsg);
            });
        }
        catch (Exception ex)
        {
            if (IsStreamingUnsupported(ex) && await TryRunNonStreamingFallbackAsync(request, assistantMsg))
                return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                var requestError = BuildFriendlyError(ex, "Request");
                assistantMsg.ErrorMessage = requestError;
                assistantMsg.Content = $"[Request failed] {requestError}";
                Error = requestError;
                SetFeedback(InfoBarSeverity.Error, requestError);
                assistantMsg.IsStreaming = false;
                RefreshMessage(assistantMsg);
            });
        }
        finally
        {
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;

            if (_activeConversation is not null)
            {
                _activeConversation.Messages.Clear();
                _activeConversation.Messages.AddRange(Messages);
            }
        }
    }

    private async Task<bool> TryRunNonStreamingFallbackAsync(ChatCompletionRequest request, ChatUiMessage assistantMsg)
    {
        if (_client is null)
            return false;

        try
        {
            var result = await Task.Run(() => _client.ChatCompletion(request));

            _dispatcherQueue.TryEnqueue(() =>
            {
                assistantMsg.IsStreaming = false;

                if (result.IsSuccess && result.Response is not null)
                {
                    assistantMsg.Content = result.Response.Message.Content;
                    Error = null;
                    SetFeedback(InfoBarSeverity.Warning, "Streaming is not supported by this route. Returned non-streaming response.");
                }
                else
                {
                    var fallbackError = result.Error?.Message ?? "No response from Service.";
                    assistantMsg.ErrorMessage = fallbackError;
                    assistantMsg.Content = $"[Request failed] {fallbackError}";
                    Error = fallbackError;
                    SetFeedback(InfoBarSeverity.Error, fallbackError);
                }

                RefreshMessage(assistantMsg);
            });

            return true;
        }
        catch
        {
            return false;
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
            _activeConversation.Messages.Clear();
    }

    private void RefreshMessage(ChatUiMessage msg)
    {
        var idx = Messages.IndexOf(msg);
        if (idx >= 0)
        {
            Messages.RemoveAt(idx);
            Messages.Insert(idx, msg);
        }
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

    private static bool IsStreamingUnsupported(Exception ex)
    {
        if (ex is NotSupportedException)
            return true;

        return ex.Message.Contains("specified method is not supported", StringComparison.OrdinalIgnoreCase);
    }

    private void SetFeedback(InfoBarSeverity severity, string? message)
    {
        FeedbackSeverity = severity;
        FeedbackMessage = message;
    }
}
