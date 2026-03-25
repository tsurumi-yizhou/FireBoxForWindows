using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading.Channels;
using Core.Com;
using Core.Models;

namespace Client;

/// <summary>
/// COM callback implementation for streaming chat completion events.
/// Writes events to a Channel so the client can consume them asynchronously.
/// </summary>
[GeneratedComClass]
[Guid("f1e2d3c4-b5a6-4978-8a9b-0c1d2e3f4a5b")]
internal sealed partial class StreamCallbackImpl : IFireBoxStreamCallback
{
    private readonly Channel<ChatStreamEvent> _channel;

    public ChannelReader<ChatStreamEvent> Reader => _channel.Reader;

    public StreamCallbackImpl()
    {
        _channel = Channel.CreateUnbounded<ChatStreamEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true,
        });
    }

    public void OnStarted(long requestId)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId,
            ChatStreamEventType.Started));
    }

    public void OnDelta(long requestId, string deltaText)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.Delta, DeltaText: deltaText));
    }

    public void OnReasoningDelta(long requestId, string reasoningText)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.ReasoningDelta, ReasoningText: reasoningText));
    }

    public void OnUsage(long requestId, long promptTokens, long completionTokens, long totalTokens)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.Usage,
            Usage: new Usage(promptTokens, completionTokens, totalTokens)));
    }

    public void OnCompleted(
        long requestId,
        string modelId,
        string messageRole,
        string messageContent,
        string? reasoningText,
        string finishReason,
        long usagePromptTokens,
        long usageCompletionTokens,
        long usageTotalTokens)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.Completed,
            Response: new ChatCompletionResponse(
                modelId,
                new ChatMessage(messageRole, messageContent),
                reasoningText,
                new Usage(usagePromptTokens, usageCompletionTokens, usageTotalTokens),
                finishReason)));
        _channel.Writer.TryComplete();
    }

    public void OnError(long requestId, string error)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.Error,
            Error: error));
        _channel.Writer.TryComplete();
    }

    public void OnCancelled(long requestId)
    {
        _channel.Writer.TryWrite(new ChatStreamEvent(
            requestId, ChatStreamEventType.Cancelled));
        _channel.Writer.TryComplete();
    }
}
