using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Configuration;
using Core.Com.Structs;
using Core.Dispatch;
using Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Com;

[GeneratedComClass]
[Guid(FireBoxGuids.CapabilityClass)]
public partial class FireBoxCapabilityClass : IFireBoxCapability, IDisposable
{
    /// <summary>Set by Service/Program.cs at startup.</summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    private static readonly ConcurrentDictionary<long, CancellationTokenSource> s_activeStreams = new();
    private static readonly ConcurrentDictionary<long, List<Embedding>> s_embeddingCache = new();
    private static long s_nextRequestId;
    private static long s_nextEmbeddingId;

    private long _connectionId;
    private string _processName = string.Empty;
    private string _executablePath = string.Empty;
    private bool _registered;
    private int _activeStreamCount;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterIfNeeded();
        GC.SuppressFinalize(this);
    }

    ~FireBoxCapabilityClass()
    {
        UnregisterIfNeeded();
    }

    private void UnregisterIfNeeded()
    {
        if (_registered && ServiceProvider is not null)
        {
            try
            {
                var mgr = GetConfigManager();
                mgr.UnregisterConnection(_connectionId);
            }
            catch (Exception ex)
            {
                LogComFailure(nameof(UnregisterIfNeeded), ex);
            }
            _registered = false;
        }
    }

    private IFireBoxAiDispatcher GetDispatcher() =>
        ServiceProvider?.GetRequiredService<IFireBoxAiDispatcher>()
        ?? throw new InvalidOperationException("Service not initialized.");

    private IFireBoxConfigManager GetConfigManager() =>
        ServiceProvider?.GetRequiredService<IFireBoxConfigManager>()
        ?? throw new InvalidOperationException("Service not initialized.");

    private FireBoxServiceOptions GetServiceOptions() =>
        ServiceProvider?.GetRequiredService<FireBoxServiceOptions>()
        ?? throw new InvalidOperationException("Service options not initialized.");

    /// <summary>
    /// Gets the real caller PID via CoGetCallContext / RPC_CALL_ATTRIBUTES for out-of-proc COM.
    /// Returns null if RPC info is unavailable — callers must treat this as an auth failure.
    /// </summary>
    private static (int Pid, string ProcessName, string ExePath)? IdentifyCaller()
    {
        try
        {
            var pid = GetCallerPid();
            if (pid > 0)
            {
                using var proc = System.Diagnostics.Process.GetProcessById(pid);
                var name = proc.ProcessName;
                string exePath;
                try
                {
                    exePath = proc.MainModule?.FileName ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"[FireBoxCapabilityClass] Failed to resolve caller executable path for PID {pid}: {ex.Message}");
                    exePath = string.Empty;
                }

                return (pid, name, exePath);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FireBoxCapabilityClass] Failed to identify COM caller: {ex.Message}");
        }

        return null;
    }

    private static int GetCallerPid()
    {
        var hr = Ole32_Rpc.TryGetCallerPidViaLocalRpc(out var pid);
        if (hr >= 0 && pid > 0)
            return pid;
        
        return 0;
    }

    private void EnsureRegisteredAndAllowed()
    {
        var mgr = GetConfigManager();
        if (!_registered)
        {
            var caller = IdentifyCaller();
            if (caller is null)
                throw new UnauthorizedAccessException("Unable to identify calling process. Access denied.");

            var (pid, name, exePath) = caller.Value;
            _processName = name;
            _executablePath = exePath;

            // Record first so the client appears in the admin UI even if denied.
            // New records default to IsAllowed = false, so the client is visible
            // but blocked until an admin explicitly approves it.
            mgr.RecordClientAccess(pid, _processName, _executablePath);

            if (!IsTrustedFirstPartyClient(_processName, _executablePath) &&
                !mgr.IsClientAllowed(_processName, _executablePath))
                throw new UnauthorizedAccessException($"Client '{_processName}' ({_executablePath}) is not allowed. An administrator must approve this client.");

            _connectionId = mgr.RegisterConnection(pid, _processName, _executablePath);
            _registered = true;
        }
        else
        {
            if (!IsTrustedFirstPartyClient(_processName, _executablePath) &&
                !mgr.IsClientAllowed(_processName, _executablePath))
                throw new UnauthorizedAccessException($"Client '{_processName}' ({_executablePath}) is not allowed.");
        }

        mgr.IncrementRequestCount(_connectionId);
    }

    private bool IsTrustedFirstPartyClient(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        return GetServiceOptions().IsTrustedClientProcessName(processName);
    }

    private void UpdateConnectionStreamState(bool hasActiveStream)
    {
        if (!_registered) return;
        try
        {
            GetConfigManager().SetConnectionStreamState(_connectionId, hasActiveStream);
        }
        catch (Exception ex)
        {
            LogComFailure(nameof(UpdateConnectionStreamState), ex);
        }
    }

    public string Ping(string message) => $"Pong: {message}";

    public void GetVirtualModelCount(out int count)
    {
        try
        {
            EnsureRegisteredAndAllowed();
            var dispatcher = GetDispatcher();
            count = dispatcher.ListVirtualModelsAsync().GetAwaiter().GetResult().Count;
        }
        catch (Exception ex)
        {
            LogComFailure(nameof(GetVirtualModelCount), ex);
            throw;
        }
    }

    public void GetVirtualModelAt(
        int index,
        out string virtualModelId,
        out string strategy,
        out int reasoning,
        out int toolCalling,
        out int inputFormatsMask,
        out int outputFormatsMask,
        out int available)
    {
        try
        {
            EnsureRegisteredAndAllowed();
            var dispatcher = GetDispatcher();
            var models = dispatcher.ListVirtualModelsAsync().GetAwaiter().GetResult();
            if (index < 0 || index >= models.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Virtual model index is out of range.");

            var model = models[index];
            virtualModelId = model.VirtualModelId;
            strategy = model.Strategy;
            reasoning = model.Capabilities.Reasoning ? 1 : 0;
            toolCalling = model.Capabilities.ToolCalling ? 1 : 0;
            inputFormatsMask = ModelMediaFormatMask.ToMask(model.Capabilities.InputFormats);
            outputFormatsMask = ModelMediaFormatMask.ToMask(model.Capabilities.OutputFormats);
            available = model.Available ? 1 : 0;
        }
        catch (Exception ex)
        {
            LogComFailure(nameof(GetVirtualModelAt), ex);
            throw;
        }
    }

    public void ListVirtualModels(out IntPtr modelsArray, out int count)
    {
        EnsureRegisteredAndAllowed();
        var dispatcher = GetDispatcher();
        var models = dispatcher.ListVirtualModelsAsync().GetAwaiter().GetResult();
        var structs = models.Select(Mappers.ToStruct).ToArray();
        modelsArray = NativeArrayMarshaller.CreateStructArray(structs);
        count = structs.Length;
    }

    public void GetModelCandidates(string virtualModelId, out IntPtr candidatesArray, out int count)
    {
        EnsureRegisteredAndAllowed();
        var dispatcher = GetDispatcher();
        var candidates = dispatcher.GetModelCandidatesAsync(virtualModelId).GetAwaiter().GetResult();
        var structs = candidates.Select(Mappers.ToStruct).ToArray();
        candidatesArray = NativeArrayMarshaller.CreateStructArray(structs);
        count = structs.Length;
    }

    public void ChatCompletion(
        string virtualModelId,
        string conversationText,
        float temperature,
        int maxOutputTokens,
        out ChatCompletionResultStruct result)
    {
        EnsureRegisteredAndAllowed();
        var dispatcher = GetDispatcher();
        var messages = BuildMessagesFromConversationText(conversationText);

        var request = new ChatCompletionRequest(virtualModelId, messages, null, temperature, maxOutputTokens);

        try
        {
            var r = dispatcher.ChatCompletionAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(r);
        }
        catch (Exception ex)
        {
            result = MakeErrorResult(FireBoxErrorCodes.Internal, ex.Message);
        }
    }

    public long StartChatCompletionStream(
        string virtualModelId,
        string conversationText,
        float temperature,
        int maxOutputTokens,
        IFireBoxStreamCallback callback)
    {
        EnsureRegisteredAndAllowed();
        var requestId = Interlocked.Increment(ref s_nextRequestId);
        var cts = new CancellationTokenSource();
        s_activeStreams[requestId] = cts;

        var dispatcher = GetDispatcher();
        var messages = BuildMessagesFromConversationText(conversationText);
        var request = new ChatCompletionRequest(virtualModelId, messages, null, temperature, maxOutputTokens);

        Interlocked.Increment(ref _activeStreamCount);
        UpdateConnectionStreamState(true);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in dispatcher.ChatCompletionStreamAsync(request, cts.Token))
                {
                    DispatchStreamEvent(callback, requestId, evt);
                }
            }
            catch (OperationCanceledException)
            {
                TryNotifyCallback(nameof(IFireBoxStreamCallback.OnCancelled), () => callback.OnCancelled(requestId));
            }
            catch (Exception ex)
            {
                LogComFailure(nameof(StartChatCompletionStream), ex);
                TryNotifyCallback(nameof(IFireBoxStreamCallback.OnError), () =>
                    callback.OnError(requestId, FireBoxErrorCodes.Internal, ex.Message, null, null));
            }
            finally
            {
                s_activeStreams.TryRemove(requestId, out _);
                cts.Dispose();
                if (Interlocked.Decrement(ref _activeStreamCount) <= 0)
                    UpdateConnectionStreamState(false);
            }
        });

        return requestId;
    }

    public void CancelChatCompletion(long requestId)
    {
        if (s_activeStreams.TryRemove(requestId, out var cts))
            cts.Cancel();
    }

    public void CreateEmbeddings(
        string virtualModelId,
        IntPtr inputArray,
        out EmbeddingResultStruct result)
    {
        EnsureRegisteredAndAllowed();
        var dispatcher = GetDispatcher();
        var input = ParseBstrArray(inputArray);
        var request = new EmbeddingRequest(virtualModelId, input);

        try
        {
            var r = dispatcher.CreateEmbeddingsAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(r);

            // Cache embedding vectors with a unique request ID
            if (r.Response is { Embeddings.Count: > 0 })
            {
                var embeddingId = Interlocked.Increment(ref s_nextEmbeddingId);
                s_embeddingCache[embeddingId] = r.Response.Embeddings;
                result.EmbeddingRequestId = embeddingId;
            }
        }
        catch (Exception ex)
        {
            result = default;
            result.HasResponse = 0;
            result.ErrorCode = FireBoxErrorCodes.Internal;
            result.ErrorMessage = Marshal.StringToBSTR(ex.Message);
        }
    }

    public void GetEmbeddingVectors(
        long embeddingRequestId,
        out IntPtr indicesArray,
        out IntPtr flatVectorsArray,
        out int vectorDimension)
    {
        if (!s_embeddingCache.TryRemove(embeddingRequestId, out var embeddings) || embeddings.Count == 0)
        {
            indicesArray = IntPtr.Zero;
            flatVectorsArray = IntPtr.Zero;
            vectorDimension = 0;
            return;
        }

        vectorDimension = embeddings[0].Vector.Length;

        var indices = embeddings.Select(e => e.Index).ToArray();
        indicesArray = NativeArrayMarshaller.CreateIntArray(indices);

        var flatVectors = new float[embeddings.Count * vectorDimension];
        for (var i = 0; i < embeddings.Count; i++)
            embeddings[i].Vector.CopyTo(flatVectors, i * vectorDimension);
        flatVectorsArray = NativeArrayMarshaller.CreateFloatArray(flatVectors);
    }

    public void CallFunction(
        string virtualModelId,
        string functionName,
        string functionDescription,
        string inputJson,
        string inputSchemaJson,
        string outputSchemaJson,
        float temperature,
        int maxOutputTokens,
        out FunctionCallResultStruct result)
    {
        EnsureRegisteredAndAllowed();
        var dispatcher = GetDispatcher();
        var request = new FunctionCallRequest(
            virtualModelId, functionName, functionDescription,
            inputJson, inputSchemaJson, outputSchemaJson,
            temperature, maxOutputTokens);

        try
        {
            var r = dispatcher.CallFunctionAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(r);
        }
        catch (Exception ex)
        {
            result = default;
            result.HasResponse = 0;
            result.ErrorCode = FireBoxErrorCodes.Internal;
            result.ErrorMessage = Marshal.StringToBSTR(ex.Message);
        }
    }

    // --- Helpers ---

    private static void DispatchStreamEvent(IFireBoxStreamCallback cb, long requestId, ChatStreamEvent evt)
    {
        switch (evt.Type)
        {
            case ChatStreamEventType.Started when evt.Selection is not null:
                cb.OnStarted(requestId, evt.Selection.ProviderId,
                    evt.Selection.ProviderType, evt.Selection.ProviderName, evt.Selection.ModelId);
                break;
            case ChatStreamEventType.Delta:
                cb.OnDelta(requestId, evt.DeltaText ?? string.Empty);
                break;
            case ChatStreamEventType.ReasoningDelta:
                cb.OnReasoningDelta(requestId, evt.ReasoningText ?? string.Empty);
                break;
            case ChatStreamEventType.Usage when evt.Usage is not null:
                cb.OnUsage(requestId, evt.Usage.PromptTokens, evt.Usage.CompletionTokens, evt.Usage.TotalTokens);
                break;
            case ChatStreamEventType.Completed when evt.Response is not null:
                cb.OnCompleted(requestId, evt.Response.Message.Role, evt.Response.Message.Content,
                    evt.Response.ReasoningText, evt.Response.FinishReason,
                    evt.Response.Usage.PromptTokens, evt.Response.Usage.CompletionTokens, evt.Response.Usage.TotalTokens);
                break;
            case ChatStreamEventType.Error when evt.Error is not null:
                cb.OnError(requestId, evt.Error.Code, evt.Error.Message, evt.Error.ProviderType, evt.Error.ProviderModelId);
                break;
            case ChatStreamEventType.Cancelled:
                cb.OnCancelled(requestId);
                break;
        }
    }

    private static List<ChatMessage> BuildMessagesFromConversationText(string conversationText)
    {
        if (string.IsNullOrWhiteSpace(conversationText))
            return [];

        return [new ChatMessage("user", conversationText)];
    }

    private static List<ChatMessage> ParseMessagesWithAttachments(IntPtr messagesArray, IntPtr attachmentsArray)
    {
        if (messagesArray == IntPtr.Zero) return [];
        var msgStructs = NativeArrayMarshaller.ReadStructArray<ChatMessageStruct>(messagesArray);
        var messages = msgStructs.Select(s => Mappers.ToModel(in s)).ToList();

        if (attachmentsArray != IntPtr.Zero)
        {
            var attStructs = NativeArrayMarshaller.ReadStructArray<ChatAttachmentStruct>(attachmentsArray);
            // Group attachments by messageIndex and merge into the correct message
            var grouped = attStructs.Select(att => (att.MessageIndex, Value: Mappers.ToModel(in att)))
                .GroupBy(a => a.MessageIndex);

            foreach (var group in grouped)
            {
                var idx = group.Key;
                if (idx < 0 || idx >= messages.Count) continue;
                var atts = group.Select(x => x.Value).ToList();
                messages[idx] = messages[idx] with { Attachments = atts };
            }
        }

        return messages;
    }

    private static List<string> ParseBstrArray(IntPtr nativeArray)
    {
        if (nativeArray == IntPtr.Zero) return [];
        return NativeArrayMarshaller.ReadBstrArray(nativeArray).ToList();
    }

    private static ChatCompletionResultStruct MakeErrorResult(int code, string message)
    {
        var result = default(ChatCompletionResultStruct);
        result.HasResponse = 0;
        result.ErrorCode = code;
        result.ErrorMessage = Marshal.StringToBSTR(message);
        return result;
    }

    private void TryNotifyCallback(string callbackName, Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            LogComFailure(callbackName, ex);
        }
    }

    private void LogComFailure(string operation, Exception ex)
    {
        try
        {
            var baseDir = GetServiceOptions().ResolveStorageRootPath();
            Directory.CreateDirectory(baseDir);
            var logPath = GetServiceOptions().ResolveComErrorLogPath();
            var text = $"{DateTimeOffset.Now:O} [{operation}] {ex}\n";
            File.AppendAllText(logPath, text);
        }
        catch
        {
            // best effort
        }
    }

}

internal static class FireBoxErrorCodes
{
    public const int Security = 1;
    public const int InvalidArgument = 2;
    public const int NoRoute = 3;
    public const int NoCandidate = 4;
    public const int ProviderError = 5;
    public const int Timeout = 6;
    public const int Internal = 7;
    public const int Cancelled = 8;
}

/// <summary>
/// P/Invoke for getting the real caller PID in out-of-proc COM scenarios.
/// Uses RpcServerInqCallAttributesW with RPC_CALL_ATTRIBUTES_V2 to get ClientPID.
/// </summary>
internal static class Ole32_Rpc
{
    public static int TryGetCallerPidViaLocalRpc(out int pid)
    {
        pid = 0;
        var status = I_RpcBindingInqLocalClientPID(IntPtr.Zero, out var rpcPid);
        if (status == 0 && rpcPid > 0)
        {
            pid = unchecked((int)rpcPid);
            return 0;
        }

        return unchecked((int)0x80070000 | status);
    }

    [DllImport("rpcrt4.dll")]
    private static extern int I_RpcBindingInqLocalClientPID(IntPtr binding, out uint pid);
}
