using System.Text.Json;
using Core.Com;
using Service.Data.Entities;
using Windows.Storage;

namespace Service.Data;

public sealed class FireBoxConfigurationStore
{
    private const string RootContainerName = "FireBox";
    private const string SnapshotKey = "ConfigurationSnapshot";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDataContainer _roamingContainer;

    public FireBoxConfigurationStore()
    {
        try
        {
            _roamingContainer = ApplicationData.Current.RoamingSettings.CreateContainer(
                RootContainerName,
                ApplicationDataCreateDisposition.Always);
            ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.StorageMode", "Using Windows RoamingSettings backend.");
        }
        catch (Exception ex)
        {
            ServiceRuntimeLog.WriteError(
                null,
                "ConfigurationStore.StorageMode",
                ex,
                "RoamingSettings unavailable. Service requires package identity.");
            throw new InvalidOperationException(
                "FireBox Service requires package identity for RoamingSettings. Current process has no package identity.",
                ex);
        }
    }

    public async Task<TResult> ReadAsync<TResult>(Func<FireBoxConfigurationSnapshot, TResult> reader)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync().ConfigureAwait(false);
            ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.Read", $"providers={snapshot.Providers.Count}, routes={snapshot.Routes.Count}, nextProviderId={snapshot.NextProviderId}, nextRouteId={snapshot.NextRouteId}");
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
            ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.Update.Begin", $"providers={snapshot.Providers.Count}, routes={snapshot.Routes.Count}, nextProviderId={snapshot.NextProviderId}, nextRouteId={snapshot.NextRouteId}");
            var result = updater(snapshot);
            await SaveCoreAsync(snapshot).ConfigureAwait(false);
            ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.Update.End", $"providers={snapshot.Providers.Count}, routes={snapshot.Routes.Count}, nextProviderId={snapshot.NextProviderId}, nextRouteId={snapshot.NextRouteId}");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FireBoxConfigurationSnapshot> LoadCoreAsync()
    {
        if (_roamingContainer.Values.TryGetValue(SnapshotKey, out var roamingValue) &&
            roamingValue is string roamingJson &&
            !string.IsNullOrWhiteSpace(roamingJson))
        {
            ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.Load", "Loaded snapshot from roaming settings.");
            return Deserialize(roamingJson);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        ServiceRuntimeLog.WriteInfo(null, "ConfigurationStore.Load", "No snapshot in roaming settings. Using defaults.");
        return new FireBoxConfigurationSnapshot();
    }

    private async Task SaveCoreAsync(FireBoxConfigurationSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        _roamingContainer.Values[SnapshotKey] = json;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private FireBoxConfigurationSnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new FireBoxConfigurationSnapshot();

        try
        {
            return JsonSerializer.Deserialize<FireBoxConfigurationSnapshot>(json, _jsonOptions)
                ?? throw new InvalidOperationException("Configuration snapshot JSON resolved to null.");
        }
        catch (Exception ex)
        {
            ServiceRuntimeLog.WriteError(null, "ConfigurationStore.Deserialize", ex, $"jsonLength={json.Length}");
            throw;
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
