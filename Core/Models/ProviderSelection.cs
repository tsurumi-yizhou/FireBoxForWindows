namespace Core.Models;

public sealed record ProviderSelection(
    int ProviderId,
    string ProviderType,
    string ProviderName,
    string ModelId);
