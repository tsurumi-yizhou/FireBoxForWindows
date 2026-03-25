using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.Json;
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
        var messages = BuildMessagesWithRequestAttachments(request);
        var (messagesJson, attachmentsJson) = SerializeChatPayload(messages);

        try
        {
            _connection.Capability.ChatCompletion(
                request.VirtualModelId,
                messagesJson,
                attachmentsJson,
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

    // --- Chat Completion (streaming) ---

    public async IAsyncEnumerable<ChatStreamEvent> ChatCompletionStreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callback = new StreamCallbackImpl();
        long requestId;
        var messages = BuildMessagesWithRequestAttachments(request);
        var (messagesJson, attachmentsJson) = SerializeChatPayload(messages);
        try
        {
            requestId = _connection.Capability.StartChatCompletionStream(
                request.VirtualModelId,
                messagesJson,
                attachmentsJson,
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

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static (string MessagesJson, string? AttachmentsJson) SerializeChatPayload(List<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return ("[]", null);

        // Serialize messages (role + content only)
        var messageDtos = messages.Select(m => new { m.Role, m.Content }).ToArray();
        var messagesJson = JsonSerializer.Serialize(messageDtos, s_jsonOptions);

        // Serialize attachments if any exist
        var attachmentDtos = new List<object>();
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Attachments is not { Count: > 0 }) continue;
            foreach (var att in messages[i].Attachments!)
            {
                attachmentDtos.Add(new
                {
                    MessageIndex = i,
                    MediaFormat = (int)att.MediaFormat,
                    att.MimeType,
                    att.FileName,
                    att.SizeBytes,
                    Base64Data = Convert.ToBase64String(att.Data),
                });
            }
        }

        var attachmentsJson = attachmentDtos.Count > 0
            ? JsonSerializer.Serialize(attachmentDtos, s_jsonOptions)
            : null;

        return (messagesJson, attachmentsJson);
    }

    private static List<ChatMessage> BuildMessagesWithRequestAttachments(ChatCompletionRequest request)
    {
        var messages = request.Messages
            .Select(message => new ChatMessage(
                message.Role,
                message.Content,
                message.Attachments is { Count: > 0 } ? [.. message.Attachments] : null))
            .ToList();

        if (request.Attachments is not { Count: > 0 })
            return messages;

        var targetIndex = messages.FindLastIndex(message => message.Role == "user");
        if (targetIndex < 0)
        {
            messages.Add(new ChatMessage("user", string.Empty, [.. request.Attachments]));
            return messages;
        }

        var target = messages[targetIndex];
        var mergedAttachments = target.Attachments is { Count: > 0 }
            ? target.Attachments.Concat(request.Attachments).ToList()
            : [.. request.Attachments];
        messages[targetIndex] = target with { Attachments = mergedAttachments };
        return messages;
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
