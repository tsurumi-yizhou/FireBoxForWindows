using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.Models;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using CoreChatMessage = Core.Models.ChatMessage;

namespace Service.Providers;

public sealed class OpenAiGateway : IProviderGateway
{
    private readonly OpenAIClient _client;
    private readonly string _baseUrl;
    public string ProviderType => "OpenAI";

    public OpenAiGateway(string apiKey, string baseUrl)
    {
        _baseUrl = baseUrl;
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
            options.Endpoint = new Uri(baseUrl);
        _client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    public async Task<ChatCompletionResponse> ChatCompletionAsync(
        string modelId, List<CoreChatMessage> messages, List<ChatAttachment>? attachments,
        float temperature, int maxOutputTokens, CancellationToken ct)
    {
        var chatClient = _client.GetChatClient(modelId);
        var opts = BuildOptions(temperature, maxOutputTokens);
        var oaiMessages = ConvertMessages(messages, attachments);

        var result = await chatClient.CompleteChatAsync(oaiMessages, opts, ct);
        var value = result.Value;

        var text = value.Content.FirstOrDefault(c => c.Kind == ChatMessageContentPartKind.Text)?.Text ?? string.Empty;

        return new ChatCompletionResponse(
            string.Empty,
            new CoreChatMessage("assistant", text),
            null,
            new ProviderSelection(0, "OpenAI", string.Empty, modelId),
            new Usage(value.Usage.InputTokenCount, value.Usage.OutputTokenCount,
                value.Usage.InputTokenCount + value.Usage.OutputTokenCount),
            value.FinishReason.ToString() ?? "stop");
    }

    public async IAsyncEnumerable<StreamChunk> ChatCompletionStreamAsync(
        string modelId, List<CoreChatMessage> messages, List<ChatAttachment>? attachments,
        float temperature, int maxOutputTokens, [EnumeratorCancellation] CancellationToken ct)
    {
        var chatClient = _client.GetChatClient(modelId);
        var opts = BuildOptions(temperature, maxOutputTokens);
        var oaiMessages = ConvertMessages(messages, attachments);

        await foreach (var update in chatClient.CompleteChatStreamingAsync(oaiMessages, opts, ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (part.Kind == ChatMessageContentPartKind.Text && part.Text is not null)
                    yield return new StreamChunk(StreamChunkType.Delta, DeltaText: part.Text);
            }

            if (update.Usage is { } usage)
            {
                yield return new StreamChunk(StreamChunkType.Usage,
                    PromptTokens: usage.InputTokenCount,
                    CompletionTokens: usage.OutputTokenCount);
            }

            if (update.FinishReason is not null)
            {
                yield return new StreamChunk(StreamChunkType.Done, FinishReason: update.FinishReason.ToString());
            }
        }
    }

    public async Task<EmbeddingResponse> CreateEmbeddingsAsync(
        string modelId, List<string> input, CancellationToken ct)
    {
        var embeddingClient = _client.GetEmbeddingClient(modelId);
        var result = await embeddingClient.GenerateEmbeddingsAsync(input, cancellationToken: ct);

        var embeddings = result.Value.Select((e, i) =>
            new Embedding(i, e.ToFloats().ToArray())).ToList();

        return new EmbeddingResponse(
            string.Empty, embeddings,
            new ProviderSelection(0, "OpenAI", string.Empty, modelId),
            new Usage(result.Value.Usage.InputTokenCount, 0, result.Value.Usage.InputTokenCount));
    }

    public async Task<FunctionCallResponse> CallFunctionAsync(
        string modelId, string functionName, string functionDescription,
        string inputJson, string inputSchemaJson, string outputSchemaJson,
        float temperature, int maxOutputTokens, CancellationToken ct)
    {
        var chatClient = _client.GetChatClient(modelId);
        var systemPrompt = $"You are a function executor. Call the function '{functionName}' ({functionDescription}) with the given input and return the output as JSON matching the output schema.\nInput schema: {inputSchemaJson}\nOutput schema: {outputSchemaJson}";

        var oaiMessages = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(inputJson),
        };

        var opts = BuildOptions(temperature, maxOutputTokens);
        var result = await chatClient.CompleteChatAsync(oaiMessages, opts, ct);
        var value = result.Value;
        var text = value.Content.FirstOrDefault(c => c.Kind == ChatMessageContentPartKind.Text)?.Text ?? string.Empty;

        return new FunctionCallResponse(
            string.Empty, text,
            new ProviderSelection(0, "OpenAI", string.Empty, modelId),
            new Usage(value.Usage.InputTokenCount, value.Usage.OutputTokenCount,
                value.Usage.InputTokenCount + value.Usage.OutputTokenCount),
            value.FinishReason.ToString() ?? "stop");
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            var result = await _client.GetOpenAIModelClient().GetModelsAsync(ct);
            return result.Value.OrderBy(m => m.Id).Select(m => m.Id).ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse model list from endpoint '{_baseUrl}'. Response is not JSON. " +
                "For OpenAI-compatible gateways, fill the complete API Base URL including version path (for example /v1 or /v2).",
                ex);
        }
    }

    private static ChatCompletionOptions BuildOptions(float temperature, int maxOutputTokens)
    {
        var opts = new ChatCompletionOptions();
        if (temperature >= 0) opts.Temperature = temperature;
        if (maxOutputTokens > 0) opts.MaxOutputTokenCount = maxOutputTokens;
        return opts;
    }

    private static List<OpenAI.Chat.ChatMessage> ConvertMessages(
        List<CoreChatMessage> messages, List<ChatAttachment>? _)
    {
        var result = new List<OpenAI.Chat.ChatMessage>();
        foreach (var msg in messages)
        {
            if (msg.Role == "system")
            {
                result.Add(new SystemChatMessage(msg.Content));
                continue;
            }
            if (msg.Role == "assistant")
            {
                result.Add(new AssistantChatMessage(msg.Content));
                continue;
            }

            // User message — include per-message attachments
            var parts = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(msg.Content) };
            if (msg.Attachments is { Count: > 0 })
            {
                foreach (var att in msg.Attachments)
                {
                    if (att.MediaFormat == ModelMediaFormat.Image)
                    {
                        var dataUri = $"data:{att.MimeType};base64,{Convert.ToBase64String(att.Data)}";
                        parts.Add(ChatMessageContentPart.CreateImagePart(new Uri(dataUri)));
                    }
                }
            }
            result.Add(new UserChatMessage(parts));
        }
        return result;
    }
}
