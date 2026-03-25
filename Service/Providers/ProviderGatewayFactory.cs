using Service.Data;
using Core.Models;

namespace Service.Providers;

public sealed class ProviderGatewayFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ProviderBaseUrlNormalizer _baseUrlNormalizer;

    public ProviderGatewayFactory(
        IHttpClientFactory httpFactory,
        ProviderBaseUrlNormalizer baseUrlNormalizer)
    {
        _httpFactory = httpFactory;
        _baseUrlNormalizer = baseUrlNormalizer;
    }

    public IProviderGateway Create(string providerType, string apiKey, string baseUrl)
    {
        var normalizedUrl = _baseUrlNormalizer.Normalize(baseUrl);
        return providerType switch
        {
            FireBoxProviderTypes.OpenAI => new OpenAiGateway(apiKey, normalizedUrl),
            FireBoxProviderTypes.Anthropic => new AnthropicGateway(apiKey, normalizedUrl),
            FireBoxProviderTypes.Gemini => new GeminiGateway(apiKey, normalizedUrl, _httpFactory.CreateClient()),
            _ => throw new ArgumentException($"Unknown provider type: {providerType}"),
        };
    }
}
