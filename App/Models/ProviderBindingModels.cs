using System;

namespace App.Models;

internal sealed record ProviderBindingRequest(
    string ProviderType,
    string ProviderDisplayName,
    string Name,
    string BaseUrl,
    string ApiKey);

internal sealed record ProviderBindingActivation(
    string SourceUri,
    ProviderBindingRequest? Request,
    string? ErrorMessage)
{
    public bool IsValid => Request is not null && string.IsNullOrWhiteSpace(ErrorMessage);
}
