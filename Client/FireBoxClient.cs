using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.Com;
using Core.Com.Structs;
using Core.Models;

namespace Client;

public sealed class FireBoxClient : IDisposable
{
    private readonly CapabilityConnection _connection;
    private bool _disposed;

    public FireBoxClient()
    {
        _connection = new CapabilityConnection();
    }

    public bool IsConnected => _connection.IsConnected;

    public string? LastWarningMessage => _connection.LastWarningMessage;

    public void Connect() => _connection.Connect();

    public string Ping(string message) => _connection.Capability.Ping(message);

    public List<ModelInfo> ListModels()
    {
        try
        {
            var json = _connection.Capability.ListModels();
            var payload = JsonSerializer.Deserialize<ListModelsPayload>(json, s_jsonOptions);
            return payload?.Models ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ListModels COM failed ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}): {ex.Message}", ex);
        }
    }

    public Result<ChatCompletionResponse> ChatCompletion(ChatCompletionRequest request)
    {
        try
        {
            _connection.Capability.ChatCompletion(
                request.ModelId,
                JsonSerializer.Serialize(request.Messages, s_jsonOptions),
                request.Temperature,
                request.MaxOutputTokens,
                (int)request.ReasoningEffort,
                out var resultStruct);

            try
            {
                return Mappers.ToModel(in resultStruct);
            }
            finally
            {
                Mappers.FreeBstrFields(in resultStruct);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ChatCompletion COM failed ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}): {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callback = new StreamCallbackImpl();
        long requestId;
        try
        {
            requestId = _connection.Capability.StartChatCompletionStream(
                request.ModelId,
                JsonSerializer.Serialize(request.Messages, s_jsonOptions),
                request.Temperature,
                request.MaxOutputTokens,
                (int)request.ReasoningEffort,
                callback);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"StartChatCompletionStream COM call failed ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}): {ex.Message}", ex);
        }

        ExceptionDispatchInfo? cancelFailure = null;
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _connection.Capability.CancelChatCompletion(requestId);
            }
            catch (Exception ex)
            {
                cancelFailure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        await using var enumerator = callback.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ChatStreamEvent evt;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;

                evt = enumerator.Current;
            }
            catch (OperationCanceledException) when (cancelFailure is not null)
            {
                throw new InvalidOperationException("Cancellation was requested, but the COM cancellation call failed.", cancelFailure.SourceException);
            }

            yield return evt;
        }

        cancelFailure?.Throw();
    }

    public void CancelChatCompletion(long requestId) =>
        _connection.Capability.CancelChatCompletion(requestId);

    public Result<EmbeddingResponse> CreateEmbeddings(EmbeddingRequest request)
    {
        var inputPtr = CreateBstrNativeArray(request.Input);
        try
        {
            _connection.Capability.CreateEmbeddings(request.ModelId, inputPtr, out var resultStruct);
            try
            {
                return Mappers.ToModel(in resultStruct);
            }
            finally
            {
                Mappers.FreeArraysAndBstrFields(in resultStruct);
            }
        }
        finally
        {
            NativeArrayMarshaller.DestroyBstrArray(inputPtr);
        }
    }

    public Result<FunctionCallResponse> CallFunction(FunctionCallRequest request)
    {
        _connection.Capability.CallFunction(
            request.ModelId,
            request.FunctionName,
            request.FunctionDescription,
            request.InputJson,
            request.InputSchemaJson,
            request.OutputSchemaJson,
            request.Temperature,
            request.MaxOutputTokens,
            out var resultStruct);

        try
        {
            return Mappers.ToModel(in resultStruct);
        }
        finally
        {
            Mappers.FreeBstrFields(in resultStruct);
        }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static IntPtr CreateBstrNativeArray(List<string> strings) =>
        NativeArrayMarshaller.CreateBstrArray(strings);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connection.Dispose();
    }

    private sealed class ListModelsPayload
    {
        public List<ModelInfo> Models { get; set; } = [];
    }
}
