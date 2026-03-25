using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Service.Data.Entities;

namespace Service.Data;

public sealed class FireBoxConfigRepository
{
    private readonly IDbContextFactory<FireBoxDbContext> _dbFactory;
    private readonly FireBoxConfigurationStore _configurationStore;
    private readonly SecureKeyStore _keyStore;
    private readonly Core.Configuration.FireBoxServiceOptions _serviceOptions;

    public FireBoxConfigRepository(
        IDbContextFactory<FireBoxDbContext> dbFactory,
        FireBoxConfigurationStore configurationStore,
        SecureKeyStore keyStore,
        Core.Configuration.FireBoxServiceOptions serviceOptions)
    {
        _dbFactory = dbFactory;
        _configurationStore = configurationStore;
        _keyStore = keyStore;
        _serviceOptions = serviceOptions;
    }

    // --- Providers ---

    public async Task<List<ProviderConfigEntity>> ListProvidersAsync()
    {
        return await _configurationStore.ReadAsync(snapshot =>
            snapshot.Providers
                .OrderBy(static provider => provider.Id)
                .Select(CloneProvider)
                .ToList());
    }

    public async Task<ProviderConfigEntity?> GetProviderAsync(int id)
    {
        return await _configurationStore.ReadAsync(snapshot =>
        {
            var provider = snapshot.Providers.FirstOrDefault(provider => provider.Id == id);
            return provider is null ? null : CloneProvider(provider);
        });
    }

    public async Task<int> AddProviderAsync(string providerType, string name, string baseUrl, string apiKey, List<string>? enabledModelIds = null)
    {
        return await _configurationStore.UpdateAsync(snapshot =>
        {
            var entity = new ProviderConfigEntity
            {
                Id = snapshot.NextProviderId++,
                ProviderType = providerType,
                Name = name,
                BaseUrl = baseUrl,
                EncryptedApiKey = _keyStore.Encrypt(apiKey),
                EnabledModelIdsJson = JsonSerializer.Serialize(enabledModelIds ?? []),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            snapshot.Providers.Add(entity);
            return entity.Id;
        });
    }

    public async Task UpdateProviderAsync(int id, string? name = null, string? baseUrl = null, string? apiKey = null, List<string>? enabledModelIds = null)
    {
        await _configurationStore.UpdateAsync<object?>(snapshot =>
        {
            var entity = snapshot.Providers.FirstOrDefault(provider => provider.Id == id)
                ?? throw new KeyNotFoundException($"Provider {id} not found.");

            if (name is not null)
                entity.Name = name;
            if (baseUrl is not null)
                entity.BaseUrl = baseUrl;
            if (apiKey is not null)
                entity.EncryptedApiKey = _keyStore.Encrypt(apiKey);
            if (enabledModelIds is not null)
                entity.EnabledModelIdsJson = JsonSerializer.Serialize(enabledModelIds);

            entity.UpdatedAt = DateTimeOffset.UtcNow;
            return null;
        });
    }

    public async Task DeleteProviderAsync(int id)
    {
        await _configurationStore.UpdateAsync<object?>(snapshot =>
        {
            snapshot.Providers.RemoveAll(provider => provider.Id == id);
            return null;
        });
    }

    public string DecryptApiKey(ProviderConfigEntity provider) =>
        _keyStore.Decrypt(provider.EncryptedApiKey);

    public List<string> GetEnabledModelIds(ProviderConfigEntity provider)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(provider.EnabledModelIdsJson) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Provider '{provider.Name}' has invalid enabled models JSON.",
                ex);
        }
    }

    // --- Routes ---

    public async Task<List<RouteRuleEntity>> ListRoutesAsync()
    {
        return await _configurationStore.ReadAsync(snapshot =>
            snapshot.Routes
                .OrderBy(static route => route.RouteId, StringComparer.OrdinalIgnoreCase)
                .Select(CloneRoute)
                .ToList());
    }

    public async Task<RouteRuleEntity?> GetRouteByRouteIdAsync(string routeId)
    {
        return await _configurationStore.ReadAsync(snapshot =>
        {
            var route = snapshot.Routes.FirstOrDefault(route =>
                string.Equals(route.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
            return route is null ? null : CloneRoute(route);
        });
    }

    public async Task<int> AddRouteAsync(RouteRuleEntity route)
    {
        return await _configurationStore.UpdateAsync(snapshot =>
        {
            var entity = CloneRoute(route);
            entity.Id = snapshot.NextRouteId++;
            entity.CreatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            snapshot.Routes.Add(entity);
            return entity.Id;
        });
    }

    public async Task UpdateRouteAsync(RouteRuleEntity route)
    {
        await _configurationStore.UpdateAsync<object?>(snapshot =>
        {
            var entity = snapshot.Routes.FirstOrDefault(existing => existing.Id == route.Id)
                ?? throw new KeyNotFoundException($"Route {route.Id} not found.");

            entity.RouteId = route.RouteId;
            entity.Strategy = route.Strategy;
            entity.CandidatesJson = route.CandidatesJson;
            entity.Reasoning = route.Reasoning;
            entity.ToolCalling = route.ToolCalling;
            entity.InputFormatsMask = route.InputFormatsMask;
            entity.OutputFormatsMask = route.OutputFormatsMask;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            return null;
        });
    }

    public async Task DeleteRouteAsync(int id)
    {
        await _configurationStore.UpdateAsync<object?>(snapshot =>
        {
                snapshot.Routes.RemoveAll(route => route.Id == id);
            return null;
        });
    }

    public List<RouteCandidateInfo> GetCandidates(RouteRuleEntity route)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RouteCandidateInfo>>(route.CandidatesJson) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Route '{route.RouteId}' has invalid candidate targets JSON.",
                ex);
        }
    }

    // --- Client Access ---

    public async Task<List<ClientAccessEntity>> ListClientAccessAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var records = await db.ClientAccess.ToListAsync();
        return records.OrderByDescending(c => c.LastSeenAt).ToList();
    }

    public async Task<ClientAccessEntity?> GetClientAccessAsync(string processName, string executablePath)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidates = await db.ClientAccess
            .Where(c => c.ProcessName == processName)
            .ToListAsync();

        return candidates.FirstOrDefault(c =>
            string.Equals(c.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RecordClientAccessAsync(int processId, string processName, string executablePath)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidates = await db.ClientAccess
            .Where(c => c.ProcessName == processName)
            .ToListAsync();
        var entity = candidates.FirstOrDefault(c =>
            string.Equals(c.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        if (entity is not null)
        {
            entity.ProcessId = processId;
            entity.ExecutablePath = executablePath;
            entity.RequestCount++;
            entity.LastSeenAt = DateTimeOffset.UtcNow;
        }
        else
        {
            db.ClientAccess.Add(new ClientAccessEntity
            {
                ProcessId = processId,
                ProcessName = processName,
                ExecutablePath = executablePath,
                RequestCount = 1,
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task UpdateClientAccessAllowedAsync(int id, bool isAllowed)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.ClientAccess.FindAsync(id);
        if (entity is not null)
        {
            entity.IsAllowed = isAllowed;
            entity.DeniedUntilUtc = isAllowed ? null : DateTimeOffset.UtcNow.Add(_serviceOptions.ResolveAccessDenyCooldown());
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdateClientAccessAllowedAsync(string processName, string executablePath, bool isAllowed)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidates = await db.ClientAccess
            .Where(c => c.ProcessName == processName)
            .ToListAsync();

        var entity = candidates.FirstOrDefault(c =>
            string.Equals(c.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

        if (entity is not null)
        {
            entity.IsAllowed = isAllowed;
            entity.DeniedUntilUtc = isAllowed ? null : DateTimeOffset.UtcNow.Add(_serviceOptions.ResolveAccessDenyCooldown());
            await db.SaveChangesAsync();
        }
    }

    public async Task SetClientAccessDecisionAsync(
        string processName,
        string executablePath,
        bool isAllowed,
        DateTimeOffset? deniedUntilUtc)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidates = await db.ClientAccess
            .Where(c => c.ProcessName == processName)
            .ToListAsync();

        var entity = candidates.FirstOrDefault(c =>
            string.Equals(c.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
            return;

        entity.IsAllowed = isAllowed;
        entity.DeniedUntilUtc = deniedUntilUtc;
        await db.SaveChangesAsync();
    }

    private static ProviderConfigEntity CloneProvider(ProviderConfigEntity provider) =>
        new()
        {
            Id = provider.Id,
            ProviderType = provider.ProviderType,
            Name = provider.Name,
            BaseUrl = provider.BaseUrl,
            EncryptedApiKey = provider.EncryptedApiKey.ToArray(),
            EnabledModelIdsJson = provider.EnabledModelIdsJson,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
        };

    private static RouteRuleEntity CloneRoute(RouteRuleEntity route) =>
        new()
        {
            Id = route.Id,
            RouteId = route.RouteId,
            Strategy = route.Strategy,
            CandidatesJson = route.CandidatesJson,
            Reasoning = route.Reasoning,
            ToolCalling = route.ToolCalling,
            InputFormatsMask = route.InputFormatsMask,
            OutputFormatsMask = route.OutputFormatsMask,
            CreatedAt = route.CreatedAt,
            UpdatedAt = route.UpdatedAt,
        };
}

public sealed record RouteCandidateInfo(int ProviderId, string ModelId);
