using System.Text.Json;
using Core.Configuration;
using Service.Data.Entities;
using Windows.Storage;

namespace Service.Data;

public sealed class FireBoxConfigurationStore
{
    private const string RootContainerName = "FireBox";
    private const string SnapshotKey = "ConfigurationSnapshot";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _fallbackFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDataContainer? _roamingContainer;

    public FireBoxConfigurationStore(FireBoxServiceOptions serviceOptions)
    {
        _fallbackFilePath = Path.Combine(serviceOptions.ResolveStorageRootPath(), "config-fallback.json");
        _roamingContainer = TryCreateRoamingContainer();
    }

    public async Task<TResult> ReadAsync<TResult>(Func<FireBoxConfigurationSnapshot, TResult> reader)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync().ConfigureAwait(false);
            return reader(snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<TResult>(Func<FireBoxConfigurationSnapshot, TResult> updater)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync().ConfigureAwait(false);
            var result = updater(snapshot);
            await SaveCoreAsync(snapshot).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FireBoxConfigurationSnapshot> LoadCoreAsync()
    {
        if (_roamingContainer is not null &&
            _roamingContainer.Values.TryGetValue(SnapshotKey, out var roamingValue) &&
            roamingValue is string roamingJson &&
            !string.IsNullOrWhiteSpace(roamingJson))
        {
            return Deserialize(roamingJson);
        }

        if (!File.Exists(_fallbackFilePath))
            return new FireBoxConfigurationSnapshot();

        var fallbackJson = await File.ReadAllTextAsync(_fallbackFilePath).ConfigureAwait(false);
        var snapshot = Deserialize(fallbackJson);

        if (_roamingContainer is not null)
        {
            _roamingContainer.Values[SnapshotKey] = JsonSerializer.Serialize(snapshot, _jsonOptions);
            TryDeleteFallbackFile();
        }

        return snapshot;
    }

    private async Task SaveCoreAsync(FireBoxConfigurationSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        if (_roamingContainer is not null)
        {
            _roamingContainer.Values[SnapshotKey] = json;
            TryDeleteFallbackFile();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_fallbackFilePath)!);
        await File.WriteAllTextAsync(_fallbackFilePath, json).ConfigureAwait(false);
    }

    private FireBoxConfigurationSnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new FireBoxConfigurationSnapshot();

        return JsonSerializer.Deserialize<FireBoxConfigurationSnapshot>(json, _jsonOptions)
            ?? new FireBoxConfigurationSnapshot();
    }

    private static ApplicationDataContainer? TryCreateRoamingContainer()
    {
        try
        {
            return ApplicationData.Current.RoamingSettings.CreateContainer(
                RootContainerName,
                ApplicationDataCreateDisposition.Always);
        }
        catch
        {
            return null;
        }
    }

    private void TryDeleteFallbackFile()
    {
        try
        {
            if (File.Exists(_fallbackFilePath))
                File.Delete(_fallbackFilePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

}

public sealed class FireBoxConfigurationSnapshot
{
    public int NextProviderId { get; set; } = 1;
    public int NextRouteId { get; set; } = 1;
    public List<ProviderConfigEntity> Providers { get; set; } = [];
    public List<RouteRuleEntity> Routes { get; set; } = [];
}