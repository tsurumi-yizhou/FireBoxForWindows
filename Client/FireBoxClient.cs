using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Core.Com;
using Core.Com.Structs;
using Core.Models;

namespace Client;

/// <summary>
/// High-level SDK for third-party programs to access FireBox AI capabilities over COM.
/// </summary>
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

    // --- List Models ---

    public List<VirtualModelInfo> ListModels()
    {
        try
        {
            _connection.Capability.GetVirtualModelCount(out var count);
            if (count <= 0)
                return [];

            var models = new List<VirtualModelInfo>(count);
            for (var i = 0; i < count; i++)
            {
                _connection.Capability.GetVirtualModelAt(
                    i,
                    out var virtualModelId,
                    out var strategy,
                    out var reasoning,
                    out var toolCalling,
                    out var inputFormatsMask,
                    out var outputFormatsMask,
                    out var available);

                models.Add(new VirtualModelInfo(
                    virtualModelId,
                    strategy,
                    new ModelCapabilities(
                        reasoning != 0,
                        toolCalling != 0,
                        ModelMediaFormatMask.FromMask(inputFormatsMask),
                        ModelMediaFormatMask.FromMask(outputFormatsMask)),
                    [],
                    available != 0));
            }

            return models;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ListVirtualModels COM failed ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}): {ex.Message}", ex);
        }
    }

    // --- Chat Completion (sync) ---

    public ChatCompletionResult ChatCompletion(ChatCompletionRequest request)
    {
        var conversationText = BuildConversationText(request.Messages);

        try
        {
            _connection.Capability.ChatCompletion(
                request.VirtualModelId,
                conversationText,
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
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ChatCompletion COM failed ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}): {ex.Message}", ex);
        }
    }

    // --- Chat Completion (streaming) ---

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callback = new StreamCallbackImpl();
        long requestId;

        var conversationText = BuildConversationText(request.Messages);
        try
        {
            requestId = _connection.Capability.StartChatCompletionStream(
                request.VirtualModelId,
                conversationText,
                request.Temperature,
                request.MaxOutputTokens,
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

    // --- Embeddings ---

    public EmbeddingResult CreateEmbeddings(EmbeddingRequest request)
    {
        var inputPtr = CreateBstrNativeArray(request.Input);
        try
        {
            _connection.Capability.CreateEmbeddings(
                request.VirtualModelId, inputPtr, out var resultStruct);
            EmbeddingResult result;
            try
            {
                result = Mappers.ToModel(in resultStruct);
            }
            finally
            {
                Mappers.FreeBstrFields(in resultStruct);
            }

            if (result.IsSuccess)
            {
                // Load vectors using the unique embedding request ID
                _connection.Capability.GetEmbeddingVectors(
                    resultStruct.EmbeddingRequestId,
                    out var indicesPtr, out var vectorsPtr, out var dim);

                var embeddings = ParseEmbeddingVectors(indicesPtr, vectorsPtr, dim);
                return new EmbeddingResult(result.Response! with { Embeddings = embeddings }, null);
            }
            return result;
        }
        finally
        {
            NativeArrayMarshaller.DestroyBstrArray(inputPtr);
        }
    }

    // --- Function Call ---

    public FunctionCallResult CallFunction(FunctionCallRequest request)
    {
        _connection.Capability.CallFunction(
            request.VirtualModelId,
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

    // --- Helpers ---

    private static string BuildConversationText(List<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            sb.Append('[').Append(message.Role).AppendLine("]");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static List<Embedding> ParseEmbeddingVectors(IntPtr indicesPtr, IntPtr vectorsPtr, int dim)
    {
        if (indicesPtr == IntPtr.Zero || vectorsPtr == IntPtr.Zero || dim <= 0)
        {
            NativeArrayMarshaller.DestroyArray(indicesPtr);
            NativeArrayMarshaller.DestroyArray(vectorsPtr);
            return [];
        }

        var indices = NativeArrayMarshaller.ReadIntArrayAndDestroy(indicesPtr);
        var flatVectors = NativeArrayMarshaller.ReadFloatArrayAndDestroy(vectorsPtr);

        var embeddings = new List<Embedding>(indices.Length);
        for (var i = 0; i < indices.Length; i++)
        {
            var offset = i * dim;
            var vector = new float[dim];
            Array.Copy(flatVectors, offset, vector, 0, dim);
            embeddings.Add(new Embedding(indices[i], vector));
        }

        return embeddings;
    }

    private static IntPtr CreateBstrNativeArray(List<string> strings) =>
        NativeArrayMarshaller.CreateBstrArray(strings);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

}
