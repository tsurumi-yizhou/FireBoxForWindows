using Service.Data;
using Core.Configuration;

namespace Service.Providers;

public sealed class ProviderGatewayFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ProviderBaseUrlNormalizer _baseUrlNormalizer;
    private readonly FireBoxServiceOptions _serviceOptions;

    public ProviderGatewayFactory(
        IHttpClientFactory httpFactory,
        ProviderBaseUrlNormalizer baseUrlNormalizer,
        FireBoxServiceOptions serviceOptions)
    {
        _httpFactory = httpFactory;
        _baseUrlNormalizer = baseUrlNormalizer;
        _serviceOptions = serviceOptions;
    }

    public IProviderGateway Create(string providerType, string apiKey, string baseUrl)
    {
        var normalizedUrl = _baseUrlNormalizer.Normalize(providerType, baseUrl);
        return providerType switch
        {
            "OpenAI" => new OpenAiGateway(apiKey, normalizedUrl),
            "Anthropic" => new AnthropicGateway(apiKey, normalizedUrl, _serviceOptions.AnthropicKnownModels),
            "Gemini" => new GeminiGateway(apiKey, normalizedUrl, _httpFactory.CreateClient()),
            _ => throw new ArgumentException($"Unknown provider type: {providerType}"),
        };
    }
}
