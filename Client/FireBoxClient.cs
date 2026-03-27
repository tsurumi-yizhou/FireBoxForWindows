using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Nodes;
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

    public Result<TFunctionOutput> CallFunction<TFunctionParameters, TFunctionOutput>(
        string modelId,
        string functionDescription,
        TFunctionParameters functionParameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionDescription);
        ArgumentNullException.ThrowIfNull(functionParameters);

        var functionRequest = new FunctionCallRequest(
            modelId,
            BuildFunctionName<TFunctionParameters, TFunctionOutput>(),
            functionDescription,
            JsonSerializer.Serialize(functionParameters, s_jsonOptions),
            BuildSchemaJson(typeof(TFunctionParameters)),
            BuildSchemaJson(typeof(TFunctionOutput)));

        var rawResult = CallFunction(functionRequest);
        if (!rawResult.IsSuccess || rawResult.Response is null)
            return new Result<TFunctionOutput>(default, rawResult.Error);

        if (typeof(TFunctionOutput) == typeof(string))
        {
            var textResult = TryConvertToStringOutput(rawResult.Response.OutputJson);
            if (textResult.IsSuccess && textResult.Response is not null)
                return new Result<TFunctionOutput>((TFunctionOutput)(object)textResult.Response, null);

            return new Result<TFunctionOutput>(default, textResult.Error);
        }

        try
        {
            var output = JsonSerializer.Deserialize<TFunctionOutput>(rawResult.Response.OutputJson, s_jsonOptions);
            if (output is null)
                return new Result<TFunctionOutput>(default, $"Function output could not be deserialized to '{typeof(TFunctionOutput).Name}'.");

            return new Result<TFunctionOutput>(output, null);
        }
        catch (JsonException ex)
        {
            return new Result<TFunctionOutput>(default, $"Function output JSON is invalid for '{typeof(TFunctionOutput).Name}': {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string BuildSchemaJson(Type type)
    {
        var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(s_jsonOptions, type);
        if (schemaNode is null)
            throw new InvalidOperationException($"Unable to generate schema for type '{type.FullName}'.");

        return schemaNode.ToJsonString();
    }

    private static string BuildFunctionName<TFunctionParameters, TFunctionOutput>()
    {
        var inputTypeName = typeof(TFunctionParameters).Name;
        var outputTypeName = typeof(TFunctionOutput).Name;
        return $"{inputTypeName}To{outputTypeName}";
    }

    private static Result<string> TryConvertToStringOutput(string outputJson)
    {
        var raw = outputJson?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return new Result<string>(default, "Function output is empty.");

        try
        {
            var jsonString = JsonSerializer.Deserialize<string>(raw, s_jsonOptions);
            if (!string.IsNullOrWhiteSpace(jsonString))
                return new Result<string>(jsonString.Trim(), null);
        }
        catch (JsonException)
        {
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonObject obj)
            {
                var titleProperty = obj.FirstOrDefault(static pair =>
                    string.Equals(pair.Key, "title", StringComparison.OrdinalIgnoreCase));
                var titleText = titleProperty.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(titleText))
                    return new Result<string>(titleText.Trim(), null);
            }
        }
        catch (JsonException)
        {
        }

        var unfenced = StripMarkdownCodeFence(raw).Trim();
        if (string.IsNullOrWhiteSpace(unfenced))
            return new Result<string>(default, "Function output cannot be converted to text.");

        return new Result<string>(unfenced, null);
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) ||
            !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
            return trimmed;

        var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (endFence <= firstNewLine)
            return trimmed;

        return trimmed[(firstNewLine + 1)..endFence];
    }

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
