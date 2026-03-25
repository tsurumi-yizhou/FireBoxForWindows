using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.Models;

namespace Service.Providers;

/// <summary>
/// Gemini gateway using the REST API directly (no SDK dependency).
/// Uses the generativelanguage.googleapis.com/v1beta endpoint.
/// </summary>
public sealed class GeminiGateway : IProviderGateway
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    public string ProviderType => FireBoxProviderTypes.Gemini;

    public GeminiGateway(string apiKey, string baseUrl, HttpClient http)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = http;
    }

    public async Task<ChatCompletionResponse> ChatCompletionAsync(
        string modelId, List<ChatMessage> messages,
        float temperature, int maxOutputTokens, ReasoningEffort reasoningEffort, CancellationToken ct)
    {
        var body = BuildRequestBody(messages, temperature, maxOutputTokens);
        var url = $"{_baseUrl}/v1beta/models/{modelId}:generateContent?key={_apiKey}";

        var response = await _http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var text = root.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        var usage = root.TryGetProperty("usageMetadata", out var um)
            ? new Usage(
                um.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt64() : 0,
                um.TryGetProperty("candidatesTokenCount", out var ct2) ? ct2.GetInt64() : 0,
                um.TryGetProperty("totalTokenCount", out var tt) ? tt.GetInt64() : 0)
            : new Usage(0, 0, 0);

        return new ChatCompletionResponse(
            modelId, new ChatMessage("assistant", text), null,
            usage, "stop");
    }

    public async IAsyncEnumerable<StreamChunk> ChatCompletionStreamAsync(
        string modelId, List<ChatMessage> messages,
        float temperature, int maxOutputTokens, ReasoningEffort reasoningEffort, [EnumeratorCancellation] CancellationToken ct)
    {
        var body = BuildRequestBody(messages, temperature, maxOutputTokens);
        var url = $"{_baseUrl}/v1beta/models/{modelId}:streamGenerateContent?alt=sse&key={_apiKey}";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textEl))
                        yield return new StreamChunk(StreamChunkType.Delta, DeltaText: textEl.GetString());
                }
            }

            if (root.TryGetProperty("usageMetadata", out var um))
            {
                yield return new StreamChunk(StreamChunkType.Usage,
                    PromptTokens: um.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt64() : 0,
                    CompletionTokens: um.TryGetProperty("candidatesTokenCount", out var ct2) ? ct2.GetInt64() : 0);
            }
        }

        yield return new StreamChunk(StreamChunkType.Done, FinishReason: "stop");
    }

    public Task<EmbeddingResponse> CreateEmbeddingsAsync(
        string modelId, List<string> input, CancellationToken ct)
    {
        throw new NotSupportedException("Use Gemini embedding endpoint separately.");
    }

    public async Task<FunctionCallResponse> CallFunctionAsync(
        string modelId, string functionName, string functionDescription,
        string inputJson, string inputSchemaJson, string outputSchemaJson,
        float temperature, int maxOutputTokens, CancellationToken ct)
    {
        var systemPrompt = $"You are a function executor. Call '{functionName}' ({functionDescription}) with the given input and return JSON matching the output schema.\nInput schema: {inputSchemaJson}\nOutput schema: {outputSchemaJson}";
        var messages = new List<ChatMessage>
        {
            new("user", $"{systemPrompt}\n\nInput:\n{inputJson}"),
        };
        var resp = await ChatCompletionAsync(modelId, messages, temperature, maxOutputTokens, ReasoningEffort.Default, ct);
        return new FunctionCallResponse(
            modelId, resp.Message.Content, resp.Usage, resp.FinishReason);
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        var url = $"{_baseUrl}/v1beta/models?key={_apiKey}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var arr))
        {
            foreach (var m in arr.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString() ?? string.Empty;
                // "models/gemini-1.5-pro" → "gemini-1.5-pro"
                models.Add(name.StartsWith("models/") ? name["models/".Length..] : name);
            }
        }
        return models.OrderBy(m => m).ToList();
    }

    private static object BuildRequestBody(
        List<ChatMessage> messages,
        float temperature, int maxOutputTokens)
    {
        var contents = messages.Select(m =>
        {
            var parts = new List<object> { new { text = m.Content } };

            // Add per-message image attachments as inline_data
            if (m.Attachments is { Count: > 0 })
            {
                foreach (var att in m.Attachments)
                {
                    if (att.MediaFormat == MediaFormat.Image)
                    {
                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = att.MimeType,
                                data = Convert.ToBase64String(att.Data),
                            }
                        });
                    }
                }
            }

            return new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts,
            };
        }).ToArray();

        return new
        {
            contents,
            generationConfig = BuildGenerationConfig(temperature, maxOutputTokens),
        };
    }

    private static object BuildGenerationConfig(float temperature, int maxOutputTokens)
    {
        if (temperature >= 0 && maxOutputTokens > 0)
            return new { temperature, maxOutputTokens };

        if (temperature >= 0)
            return new { temperature };

        if (maxOutputTokens > 0)
            return new { maxOutputTokens };

        return new { };
    }
}
