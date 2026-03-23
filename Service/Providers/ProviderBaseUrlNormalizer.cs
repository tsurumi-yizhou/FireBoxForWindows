using Core.Configuration;

namespace Service.Providers;

public sealed class ProviderBaseUrlNormalizer
{
    private readonly FireBoxServiceOptions _options;

    public ProviderBaseUrlNormalizer(FireBoxServiceOptions options)
    {
        _options = options;
    }

    public string Normalize(string providerType, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return _options.GetDefaultProviderBaseUrl(providerType);

        var url = baseUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        return url.TrimEnd('/');
    }
}
