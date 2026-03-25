namespace Service.Providers;

public sealed class ProviderBaseUrlNormalizer
{
    public string Normalize(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Provider base URL is required.");

        var url = baseUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsed.Host))
        {
            throw new InvalidOperationException(
                "Provider base URL must be an absolute http(s) URL. Include the full API base path explicitly.");
        }

        return parsed.ToString().TrimEnd('/');
    }
}
