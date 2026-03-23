using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Core.Models;
using AnthropicMessage = Anthropic.Models.Messages.Message;
using AnthropicUsage = Anthropic.Models.Messages.Usage;

namespace Service.Providers;

public sealed class AnthropicGateway : IProviderGateway
{
    private readonly AnthropicClient _client;
    private readonly IReadOnlyList<string> _knownModels;

    public string ProviderType => "Anthropic";

    public AnthropicGateway(string apiKey, string baseUrl, IReadOnlyList<string> knownModels)
    {
        var opts = new ClientOptions { ApiKey = apiKey };
        if (!string.IsNullOrEmpty(baseUrl))
            opts.BaseUrl = baseUrl;
        _client = new AnthropicClient(opts);
        _knownModels = knownModels;
    }

    public async Task<ChatCompletionResponse> ChatCompletionAsync(
        string modelId, List<Core.Models.ChatMessage> messages, List<ChatAttachment>? attachments,
        float temperature, int maxOutputTokens, CancellationToken ct)
    {
        var (system, msgs) = ConvertMessages(messages);
        var param = string.IsNullOrWhiteSpace(system)
            ? new MessageCreateParams
            {
                Model = modelId,
                Messages = msgs,
                MaxTokens = maxOutputTokens > 0 ? maxOutputTokens : 4096,
                Temperature = temperature >= 0 ? temperature : null,
            }
            : new MessageCreateParams
            {
                Model = modelId,
                Messages = msgs,
                MaxTokens = maxOutputTokens > 0 ? maxOutputTokens : 4096,
                Temperature = temperature >= 0 ? temperature : null,
                System = system,
            };

        var resp = await _client.Messages.Create(param, ct);
        return ToResponse(modelId, resp);
    }

    public async IAsyncEnumerable<StreamChunk> ChatCompletionStreamAsync(
        string modelId, List<Core.Models.ChatMessage> messages, List<ChatAttachment>? attachments,
        float temperature, int maxOutputTokens, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, msgs) = ConvertMessages(messages);
        var param = string.IsNullOrWhiteSpace(system)
            ? new MessageCreateParams
            {
                Model = modelId,
                Messages = msgs,
                MaxTokens = maxOutputTokens > 0 ? maxOutputTokens : 4096,
                Temperature = temperature >= 0 ? temperature : null,
            }
            : new MessageCreateParams
            {
                Model = modelId,
                Messages = msgs,
                MaxTokens = maxOutputTokens > 0 ? maxOutputTokens : 4096,
                Temperature = temperature >= 0 ? temperature : null,
                System = system,
            };

        await foreach (var evt in _client.Messages.CreateStreaming(param, ct))
        {
            if (evt.TryPickContentBlockDelta(out var blockDelta))
            {
                if (blockDelta.Delta.TryPickText(out var textDelta))
                    yield return new StreamChunk(StreamChunkType.Delta, DeltaText: textDelta.Text);
                else if (blockDelta.Delta.TryPickThinking(out var thinkDelta))
                    yield return new StreamChunk(StreamChunkType.ReasoningDelta, ReasoningText: thinkDelta.Thinking);
            }
            else if (evt.TryPickDelta(out var msgDelta))
            {
                var usage = msgDelta.Usage;
                if (usage is not null)
                {
                    long pt = usage.InputTokens is long v1 ? v1 : 0;
                    long ct2 = usage.OutputTokens is long v2 ? v2 : 0;
                    yield return new StreamChunk(StreamChunkType.Usage, PromptTokens: pt, CompletionTokens: ct2);
                }
            }
            else if (evt.TryPickStop(out _))
            {
                yield return new StreamChunk(StreamChunkType.Done, FinishReason: "stop");
            }
        }
    }

    public Task<EmbeddingResponse> CreateEmbeddingsAsync(
        string modelId, List<string> input, CancellationToken ct)
    {
        throw new NotSupportedException("Anthropic does not support embeddings.");
    }

    public Task<FunctionCallResponse> CallFunctionAsync(
        string modelId, string functionName, string functionDescription,
        string inputJson, string inputSchemaJson, string outputSchemaJson,
        float temperature, int maxOutputTokens, CancellationToken ct)
    {
        throw new NotSupportedException("Function calling via structured output is not yet supported for Anthropic.");
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        // Anthropic SDK doesn't have a list models endpoint, so FireBox reads the fallback catalog from configuration.
        return await Task.FromResult(_knownModels.OrderBy(static model => model, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ChatCompletionResponse ToResponse(string modelId, AnthropicMessage resp)
    {
        var text = ExtractText(resp.Content);
        return new ChatCompletionResponse(
            modelId,
            new Core.Models.ChatMessage("assistant", text),
            null, // reasoning
            new ProviderSelection(0, "Anthropic", "Anthropic", modelId),
            new Core.Models.Usage(resp.Usage?.InputTokens ?? 0, resp.Usage?.OutputTokens ?? 0,
                (resp.Usage?.InputTokens ?? 0) + (resp.Usage?.OutputTokens ?? 0)),
            resp.StopReason?.ToString() ?? "stop");
    }

    private static (string? System, List<MessageParam> Messages) ConvertMessages(
        List<Core.Models.ChatMessage> messages)
    {
        string? systemText = null;
        var userMessages = new List<MessageParam>();

        foreach (var msg in messages)
        {
            if (msg.Role == "system")
            {
                systemText = (systemText is null ? "" : systemText + "\n") + msg.Content;
                continue;
            }

            if (msg.Attachments is { Count: > 0 } && msg.Role == "user")
            {
                // Build multimodal content blocks
                var blocks = new List<ContentBlockParam>();
                foreach (var att in msg.Attachments)
                {
                    if (att.MediaFormat == ModelMediaFormat.Image)
                    {
                        blocks.Add(new ImageBlockParam
                        {
                            Source = new Base64ImageSource
                            {
                                Data = Convert.ToBase64String(att.Data),
                                MediaType = att.MimeType,
                            },
                        });
                    }
                }
                blocks.Add(new TextBlockParam { Text = msg.Content });
                userMessages.Add(new MessageParam { Role = msg.Role, Content = blocks });
            }
            else
            {
                userMessages.Add(new MessageParam { Role = msg.Role, Content = msg.Content });
            }
        }

        return (systemText, userMessages);
    }

    private static string ExtractText(IReadOnlyList<ContentBlock>? content)
    {
        if (content is null) return string.Empty;
        var texts = new List<string>();
        foreach (var block in content)
        {
            if (block.TryPickText(out var textBlock))
                texts.Add(textBlock.Text);
        }
        return string.Join("", texts);
    }
}
