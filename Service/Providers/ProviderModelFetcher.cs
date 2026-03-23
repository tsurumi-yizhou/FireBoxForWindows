using Service.Data;

namespace Service.Providers;

public sealed class ProviderModelFetcher
{
    private readonly ProviderGatewayFactory _gatewayFactory;
    private readonly SecureKeyStore _keyStore;

    public ProviderModelFetcher(ProviderGatewayFactory gatewayFactory, SecureKeyStore keyStore)
    {
        _gatewayFactory = gatewayFactory;
        _keyStore = keyStore;
    }

    public async Task<List<string>> FetchModelsAsync(
        string providerType, string baseUrl, byte[] encryptedApiKey, CancellationToken ct = default)
    {
        var apiKey = _keyStore.Decrypt(encryptedApiKey);
        var gateway = _gatewayFactory.Create(providerType, apiKey, baseUrl);
        return await gateway.ListModelsAsync(ct);
    }
}
