using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Core.Com;

/// <summary>
/// Callback interface for streaming chat completion events.
/// Each event type has its own method with only the relevant parameters.
/// Implemented by the client, called by the service.
/// </summary>
[GeneratedComInterface]
[Guid(FireBoxGuids.StreamCallbackInterface)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IFireBoxStreamCallback
{
    void OnStarted(
        long requestId,
        int selectionProviderId,
        [MarshalAs(UnmanagedType.BStr)] string selectionProviderType,
        [MarshalAs(UnmanagedType.BStr)] string selectionProviderName,
        [MarshalAs(UnmanagedType.BStr)] string selectionModelId);

    void OnDelta(long requestId, [MarshalAs(UnmanagedType.BStr)] string deltaText);

    void OnReasoningDelta(long requestId, [MarshalAs(UnmanagedType.BStr)] string reasoningText);

    void OnUsage(long requestId, long promptTokens, long completionTokens, long totalTokens);

    void OnCompleted(
        long requestId,
        [MarshalAs(UnmanagedType.BStr)] string messageRole,
        [MarshalAs(UnmanagedType.BStr)] string messageContent,
        [MarshalAs(UnmanagedType.BStr)] string? reasoningText,
        [MarshalAs(UnmanagedType.BStr)] string finishReason,
        long usagePromptTokens,
        long usageCompletionTokens,
        long usageTotalTokens);

    void OnError(
        long requestId,
        int errorCode,
        [MarshalAs(UnmanagedType.BStr)] string errorMessage,
        [MarshalAs(UnmanagedType.BStr)] string? errorProviderType,
        [MarshalAs(UnmanagedType.BStr)] string? errorProviderModelId);

    void OnCancelled(long requestId);
}
