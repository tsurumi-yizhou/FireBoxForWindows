using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using App.Models;
using Core.Models;

namespace App.Services;

internal static class ProviderBindingUriParser
{
    private static readonly string[] s_typeKeys = ["type", "provider", "provider_type"];
    private static readonly string[] s_nameKeys = ["name", "provider_name"];
    private static readonly string[] s_baseUrlKeys = ["base_url", "baseUrl", "url"];
    private static readonly string[] s_apiKeyKeys = ["api_key", "apiKey", "key"];

    public static bool TryParse(Uri uri, out ProviderBindingRequest? request, out string? error)
    {
        request = null;

        if (!string.Equals(uri.Scheme, "firebox", StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid FireBox link: scheme must be 'firebox'.";
            return false;
        }

        if (!string.Equals(uri.Host, "provider", StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid FireBox link: host must be 'provider'.";
            return false;
        }

        if (!IsBindPath(uri.AbsolutePath))
        {
            error = "Invalid FireBox link: path must be '/bind'.";
            return false;
        }

        var queryValues = ParseQuery(uri.Query);
        var rawType = GetFirstValue(queryValues, s_typeKeys);
        var rawName = GetFirstValue(queryValues, s_nameKeys);
        var rawBaseUrl = GetFirstValue(queryValues, s_baseUrlKeys);
        var rawApiKey = GetFirstValue(queryValues, s_apiKeyKeys);

        var baseUrl = NormalizeBaseUrl(rawBaseUrl);
        var apiKey = rawApiKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "Invalid FireBox link: 'base_url' is required and must be an absolute http(s) URL.";
            return false;
        }

        if (!TryResolveProviderType(rawType, baseUrl, out var providerType, out var providerDisplayName))
        {
            error = "Invalid FireBox link: provider 'type' is required unless the base URL clearly identifies a supported provider.";
            return false;
        }

        var name = rawName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Invalid FireBox link: provider name is required.";
            return false;
        }

        request = new ProviderBindingRequest(
            providerType,
            providerDisplayName,
            name,
            baseUrl,
            apiKey);
        error = null;
        return true;
    }

    private static bool IsBindPath(string path) =>
        string.Equals(path, "/bind", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "/bind/", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.StartsWith('?') ? query[1..] : query;

        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
            var rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;

            var key = WebUtility.UrlDecode(rawKey)?.Trim();
            if (string.IsNullOrWhiteSpace(key) || values.ContainsKey(key))
                continue;

            values[key] = WebUtility.UrlDecode(rawValue)?.Trim() ?? string.Empty;
        }

        return values;
    }

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> values, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value))
                return value;
        }

        return null;
    }

    private static bool TryResolveProviderType(
        string? explicitType,
        string baseUrl,
        out string providerType,
        out string providerDisplayName)
    {
        var candidate = explicitType?.Trim();
        if (!string.IsNullOrWhiteSpace(candidate))
            return TryMapProviderType(candidate, out providerType, out providerDisplayName);

        return TryInferProviderType(baseUrl, out providerType, out providerDisplayName);
    }

    private static bool TryMapProviderType(string value, out string providerType, out string providerDisplayName)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "openai":
                providerType = FireBoxProviderTypes.OpenAI;
                providerDisplayName = FireBoxProviderTypes.OpenAI;
                return true;
            case "anthropic":
                providerType = FireBoxProviderTypes.Anthropic;
                providerDisplayName = FireBoxProviderTypes.Anthropic;
                return true;
            case "gemini":
            case "google":
            case "googleai":
            case "google-ai":
                providerType = FireBoxProviderTypes.Gemini;
                providerDisplayName = FireBoxProviderTypes.Gemini;
                return true;
            default:
                providerType = string.Empty;
                providerDisplayName = string.Empty;
                return false;
        }
    }

    private static bool TryInferProviderType(string baseUrl, out string providerType, out string providerDisplayName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            providerType = string.Empty;
            providerDisplayName = string.Empty;
            return false;
        }

        var normalized = baseUrl.ToLowerInvariant();
        if (normalized.Contains("anthropic", StringComparison.Ordinal))
            return TryMapProviderType("anthropic", out providerType, out providerDisplayName);

        if (normalized.Contains("generativelanguage.googleapis.com", StringComparison.Ordinal) ||
            normalized.Contains("googleapis.com", StringComparison.Ordinal))
        {
            return TryMapProviderType("gemini", out providerType, out providerDisplayName);
        }

        if (normalized.Contains("openai", StringComparison.Ordinal))
            return TryMapProviderType("openai", out providerType, out providerDisplayName);

        providerType = string.Empty;
        providerDisplayName = string.Empty;
        return false;
    }

    private static string NormalizeBaseUrl(string? rawBaseUrl)
    {
        var trimmed = rawBaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            string.IsNullOrWhiteSpace(parsed.Scheme) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var path = parsed.AbsolutePath.Trim('/');
        var authority = parsed.IsDefaultPort
            ? parsed.Host
            : $"{parsed.Host}:{parsed.Port}";

        return string.IsNullOrWhiteSpace(path)
            ? $"{parsed.Scheme}://{authority}"
            : $"{parsed.Scheme}://{authority}/{path}";
    }
}
