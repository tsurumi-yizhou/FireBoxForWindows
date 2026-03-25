using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using Core.Com.Structs;
using Core.Dispatch;
using Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Com;

[GeneratedComClass]
[Guid(FireBoxGuids.CapabilityClass)]
public partial class FireBoxCapabilityClass : IFireBoxCapability, IDisposable
{
    public static IServiceProvider? ServiceProvider { get; set; }

    private static readonly ConcurrentDictionary<long, CancellationTokenSource> s_activeStreams = new();
    private static long s_nextRequestId;

    private long _connectionId;
    private string _processName = string.Empty;
    private string _executablePath = string.Empty;
    private bool _registered;
    private int _activeStreamCount;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

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
                GetConfigManager().UnregisterConnection(_connectionId);
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

    private static (int Pid, string ProcessName, string ExePath)? IdentifyCaller()
    {
        try
        {
            var pid = GetCallerPid();
            if (pid <= 0)
                return null;

            using var proc = Process.GetProcessById(pid);
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
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FireBoxCapabilityClass] Failed to identify COM caller: {ex.Message}");
            return null;
        }
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

            mgr.RecordClientAccess(pid, _processName, _executablePath);
            ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.RegisterClient", $"pid={pid}, process={_processName}, path={_executablePath}");

            if (!mgr.IsClientAllowed(_processName, _executablePath))
            {
                ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.AccessDenied", $"pid={pid}, process={_processName}, path={_executablePath}");
                throw new UnauthorizedAccessException($"Client '{_processName}' ({_executablePath}) is not allowed. An administrator must approve this client.");
            }

            _connectionId = mgr.RegisterConnection(pid, _processName, _executablePath);
            _registered = true;
            ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.ConnectionRegistered", $"connectionId={_connectionId}, pid={pid}, process={_processName}");
        }
        else if (!mgr.IsClientAllowed(_processName, _executablePath))
        {
            ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.AccessDenied", $"connectionId={_connectionId}, process={_processName}, path={_executablePath}");
            throw new UnauthorizedAccessException($"Client '{_processName}' ({_executablePath}) is not allowed.");
        }

        mgr.IncrementRequestCount(_connectionId);
    }

    private void UpdateConnectionStreamState(bool hasActiveStream)
    {
        if (!_registered)
            return;

        try
        {
            GetConfigManager().SetConnectionStreamState(_connectionId, hasActiveStream);
        }
        catch (Exception ex)
        {
            LogComFailure(nameof(UpdateConnectionStreamState), ex);
        }
    }

    public string Ping(string message)
    {
        EnsureRegisteredAndAllowed();
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.Ping", $"connectionId={_connectionId}, message={message}");
        return $"Pong: {message}";
    }

    public string ListModels()
    {
        try
        {
            EnsureRegisteredAndAllowed();
            var models = GetDispatcher().ListModelsAsync().GetAwaiter().GetResult();
            return JsonSerializer.Serialize(new { models });
        }
        catch (Exception ex)
        {
            LogComFailure(nameof(ListModels), ex);
            throw;
        }
    }

    public void ChatCompletion(
        string modelId,
        string messagesJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        out ChatCompletionResultStruct result)
    {
        EnsureRegisteredAndAllowed();
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.ChatCompletion", $"connectionId={_connectionId}, modelId={modelId}, temperature={temperature}, maxOutputTokens={maxOutputTokens}, reasoningEffort={reasoningEffort}");
        var request = new ChatCompletionRequest(
            modelId,
            DeserializeMessages(messagesJson),
            temperature,
            maxOutputTokens,
            ReasoningEfforts.Normalize(reasoningEffort));

        try
        {
            var response = GetDispatcher().ChatCompletionAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(response);
        }
        catch (Exception ex)
        {
            result = Mappers.ToStruct(new Result<ChatCompletionResponse>(null, ex.Message));
        }
    }

    public long StartChatCompletionStream(
        string modelId,
        string messagesJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        IFireBoxStreamCallback callback)
    {
        EnsureRegisteredAndAllowed();
        var requestId = Interlocked.Increment(ref s_nextRequestId);
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.StartChatCompletionStream", $"connectionId={_connectionId}, requestId={requestId}, modelId={modelId}, temperature={temperature}, maxOutputTokens={maxOutputTokens}, reasoningEffort={reasoningEffort}");
        ChatCompletionRequest request;
        try
        {
            request = new ChatCompletionRequest(
                modelId,
                DeserializeMessages(messagesJson),
                temperature,
                maxOutputTokens,
                ReasoningEfforts.Normalize(reasoningEffort));
        }
        catch (Exception ex)
        {
            TryNotifyCallback(nameof(IFireBoxStreamCallback.OnError), () => callback.OnError(requestId, ex.Message));
            return requestId;
        }

        var cts = new CancellationTokenSource();
        s_activeStreams[requestId] = cts;
        Interlocked.Increment(ref _activeStreamCount);
        UpdateConnectionStreamState(true);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in GetDispatcher().ChatCompletionStreamAsync(request, cts.Token))
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
                TryNotifyCallback(nameof(IFireBoxStreamCallback.OnError), () => callback.OnError(requestId, ex.Message));
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
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.CancelChatCompletion", $"requestId={requestId}");
        if (s_activeStreams.TryRemove(requestId, out var cts))
            cts.Cancel();
    }

    public void CreateEmbeddings(string modelId, IntPtr inputArray, out EmbeddingResultStruct result)
    {
        EnsureRegisteredAndAllowed();
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.CreateEmbeddings", $"connectionId={_connectionId}, modelId={modelId}");
        var request = new EmbeddingRequest(modelId, ParseBstrArray(inputArray));

        try
        {
            var response = GetDispatcher().CreateEmbeddingsAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(response);
        }
        catch (Exception ex)
        {
            result = Mappers.ToStruct(new Result<EmbeddingResponse>(null, ex.Message));
        }
    }

    public void CallFunction(
        string modelId,
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
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Capability.CallFunction", $"connectionId={_connectionId}, modelId={modelId}, functionName={functionName}, maxOutputTokens={maxOutputTokens}");
        var request = new FunctionCallRequest(
            modelId,
            functionName,
            functionDescription,
            inputJson,
            inputSchemaJson,
            outputSchemaJson,
            temperature,
            maxOutputTokens);

        try
        {
            var response = GetDispatcher().CallFunctionAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            result = Mappers.ToStruct(response);
        }
        catch (Exception ex)
        {
            result = Mappers.ToStruct(new Result<FunctionCallResponse>(null, ex.Message));
        }
    }

    private static void DispatchStreamEvent(IFireBoxStreamCallback callback, long requestId, ChatStreamEvent evt)
    {
        switch (evt.Type)
        {
            case ChatStreamEventType.Started:
                callback.OnStarted(requestId);
                break;
            case ChatStreamEventType.Delta:
                callback.OnDelta(requestId, evt.DeltaText ?? string.Empty);
                break;
            case ChatStreamEventType.ReasoningDelta:
                callback.OnReasoningDelta(requestId, evt.ReasoningText ?? string.Empty);
                break;
            case ChatStreamEventType.Usage when evt.Usage is not null:
                callback.OnUsage(requestId, evt.Usage.PromptTokens, evt.Usage.CompletionTokens, evt.Usage.TotalTokens);
                break;
            case ChatStreamEventType.Completed when evt.Response is not null:
                callback.OnCompleted(
                    requestId,
                    evt.Response.ModelId,
                    evt.Response.Message.Role,
                    evt.Response.Message.Content,
                    evt.Response.ReasoningText,
                    evt.Response.FinishReason,
                    evt.Response.Usage.PromptTokens,
                    evt.Response.Usage.CompletionTokens,
                    evt.Response.Usage.TotalTokens);
                break;
            case ChatStreamEventType.Error:
                callback.OnError(requestId, evt.Error ?? "Unknown error");
                break;
            case ChatStreamEventType.Cancelled:
                callback.OnCancelled(requestId);
                break;
        }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static List<ChatMessage> DeserializeMessages(string messagesJson)
    {
        if (string.IsNullOrWhiteSpace(messagesJson))
            return [];

        return JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, s_jsonOptions) ?? [];
    }

    private static List<string> ParseBstrArray(IntPtr nativeArray)
    {
        if (nativeArray == IntPtr.Zero)
            return [];

        return NativeArrayMarshaller.ReadBstrArray(nativeArray).ToList();
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
        ServiceRuntimeLog.WriteError(
            ServiceProvider,
            $"Capability.{operation}",
            ex,
            $"connectionId={_connectionId}, process={_processName}, path={_executablePath}");
    }
}

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
