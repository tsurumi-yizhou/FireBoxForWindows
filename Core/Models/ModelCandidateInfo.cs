namespace Core.Models;

public sealed record ModelCandidateInfo(
    int ProviderId,
    string ProviderType,
    string ProviderName,
    string BaseUrl,
    string ModelId,
    bool EnabledInConfig,
    bool CapabilitySupported);
