using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using Core.Com;
using Core.Configuration;
using Core.Dispatch;
using Core.Models;
using Service.Data;
using Service.Data.Entities;
using Service.Providers;

namespace Service.Dispatch;

public sealed class FireBoxConfigManager : IFireBoxConfigManager
{
    private readonly FireBoxConfigRepository _configRepo;
    private readonly FireBoxStatsRepository _statsRepo;
    private readonly ProviderModelFetcher _modelFetcher;
    private readonly ProviderBaseUrlNormalizer _baseUrlNormalizer;
    private readonly ConnectionStateHolder _connections;
    private readonly FireBoxServiceOptions _serviceOptions;

    public FireBoxConfigManager(
        FireBoxConfigRepository configRepo,
        FireBoxStatsRepository statsRepo,
        ProviderModelFetcher modelFetcher,
        ProviderBaseUrlNormalizer baseUrlNormalizer,
        ConnectionStateHolder connections,
        FireBoxServiceOptions serviceOptions)
    {
        _configRepo = configRepo;
        _statsRepo = statsRepo;
        _modelFetcher = modelFetcher;
        _baseUrlNormalizer = baseUrlNormalizer;
        _connections = connections;
        _serviceOptions = serviceOptions;
    }

    public string ListProviders()
    {
        var providers = _configRepo.ListProvidersAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(providers.Select(p => new
        {
            p.Id, p.ProviderType, p.Name, p.BaseUrl,
            EnabledModelIds = _configRepo.GetEnabledModelIds(p),
            p.CreatedAt, p.UpdatedAt,
        }));
    }

    public int GetVersionCode()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
            return 1;

        var build = version.Build < 0 ? 0 : version.Build;
        var revision = version.Revision < 0 ? 0 : version.Revision;
        return (version.Major * 1_000_000) + (version.Minor * 10_000) + (build * 100) + revision;
    }

    public int AddProvider(string providerType, string name, string baseUrl, string apiKey) =>
        Execute("AddProvider", $"providerType={providerType}, name={name}, baseUrl={(string.IsNullOrWhiteSpace(baseUrl) ? "<empty>" : baseUrl)}", () =>
            _configRepo.AddProviderAsync(providerType, name, NormalizeRequiredBaseUrl(baseUrl), apiKey).GetAwaiter().GetResult());

    public void UpdateProvider(int id, string name, string baseUrl, string? apiKey, bool apiKeyProvided, string enabledModelIdsJson)
    {
        var enabledModelIds = string.IsNullOrEmpty(enabledModelIdsJson)
            ? null
            : JsonSerializer.Deserialize<List<string>>(enabledModelIdsJson);
        Execute("UpdateProvider", $"id={id}, name={name}, baseUrl={baseUrl}, apiKeyProvided={apiKeyProvided}, enabledModelCount={enabledModelIds?.Count ?? 0}", () =>
            _configRepo.UpdateProviderAsync(id,
                string.IsNullOrEmpty(name) ? null : name,
                string.IsNullOrEmpty(baseUrl) ? null : NormalizeRequiredBaseUrl(baseUrl),
                apiKeyProvided ? apiKey : null,
                enabledModelIds).GetAwaiter().GetResult());
    }

    public void DeleteProvider(int id) =>
        Execute("DeleteProvider", $"id={id}", () => _configRepo.DeleteProviderAsync(id).GetAwaiter().GetResult());

    public string FetchProviderModels(int providerId)
    {
        return Execute("FetchProviderModels", $"providerId={providerId}", () =>
        {
            var provider = _configRepo.GetProviderAsync(providerId).GetAwaiter().GetResult()
                ?? throw new KeyNotFoundException($"Provider {providerId} not found.");
            var models = _modelFetcher.FetchModelsAsync(provider.ProviderType, provider.BaseUrl, provider.EncryptedApiKey)
                .GetAwaiter().GetResult();
            return JsonSerializer.Serialize(models);
        });
    }

    public string ListRoutes()
    {
        var routes = _configRepo.ListRoutesAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(routes.Select(r => new
        {
            r.Id, r.RouteId, Strategy = FireBoxRouteStrategies.Normalize(r.Strategy),
            Candidates = _configRepo.GetCandidates(r),
            r.Reasoning, r.ToolCalling,
            r.InputFormatsMask, r.OutputFormatsMask,
            r.CreatedAt, r.UpdatedAt,
        }));
    }

    public int AddRoute(string routeId, string strategy, string candidatesJson,
        bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("AddRoute", $"routeId={routeId}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", () =>
            _configRepo.AddRouteAsync(new RouteRuleEntity
            {
                RouteId = routeId,
                Strategy = FireBoxRouteStrategies.Normalize(strategy),
                CandidatesJson = candidatesJson ?? "[]",
                Reasoning = reasoning,
                ToolCalling = toolCalling,
                InputFormatsMask = inputFormatsMask,
                OutputFormatsMask = outputFormatsMask,
            }).GetAwaiter().GetResult());

    public void UpdateRoute(int id, string routeId, string strategy, string candidatesJson,
        bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("UpdateRoute", $"id={id}, routeId={routeId}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", () =>
            _configRepo.UpdateRouteAsync(new RouteRuleEntity
            {
                Id = id,
                RouteId = routeId,
                Strategy = FireBoxRouteStrategies.Normalize(strategy),
                CandidatesJson = candidatesJson ?? "[]",
                Reasoning = reasoning,
                ToolCalling = toolCalling,
                InputFormatsMask = inputFormatsMask,
                OutputFormatsMask = outputFormatsMask,
            }).GetAwaiter().GetResult());

    public void DeleteRoute(int id) =>
        Execute("DeleteRoute", $"id={id}", () => _configRepo.DeleteRouteAsync(id).GetAwaiter().GetResult());

    public string GetDailyStats(int year, int month, int day)
    {
        var stats = _statsRepo.GetDailyStatsAsync(new DateOnly(year, month, day)).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(stats);
    }

    public string GetMonthlyStats(int year, int month)
    {
        var stats = _statsRepo.GetMonthlyStatsAsync(year, month).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(stats);
    }

    public string ListConnections()
    {
        var connections = _connections.GetActiveConnections();
        return JsonSerializer.Serialize(connections.Select(c => new
        {
            c.ConnectionId, c.ProcessId, c.ProcessName, c.ExecutablePath,
            c.ConnectedAt, c.RequestCount, c.HasActiveStream,
        }));
    }

    public string ListClientAccess()
    {
        var records = _configRepo.ListClientAccessAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(records);
    }

    public void UpdateClientAccessAllowed(int accessId, bool isAllowed) =>
        Execute("UpdateClientAccessAllowed", $"accessId={accessId}, isAllowed={isAllowed}", () =>
            _configRepo.UpdateClientAccessAllowedAsync(accessId, isAllowed).GetAwaiter().GetResult());

    public long RegisterConnection(int processId, string processName, string executablePath) =>
        _connections.RegisterConnection(processId, processName, executablePath);

    public void UnregisterConnection(long connectionId) =>
        _connections.UnregisterConnection(connectionId);

    public void IncrementRequestCount(long connectionId) =>
        _connections.IncrementRequestCount(connectionId);

    public bool IsClientAllowed(string processName, string executablePath)
    {
        var record = _configRepo.GetClientAccessAsync(processName, executablePath).GetAwaiter().GetResult();

        if (record?.IsAllowed == true)
            return true;

        if (record?.DeniedUntilUtc is { } deniedUntil && deniedUntil > DateTimeOffset.UtcNow)
            return false;

        return TryApproveClientViaTofu(processName, executablePath);
    }

    public void RecordClientAccess(int processId, string processName, string executablePath) =>
        _configRepo.RecordClientAccessAsync(processId, processName, executablePath).GetAwaiter().GetResult();

    private bool TryApproveClientViaTofu(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var title = "FireBox Service Trust Request";
        var pathText = string.IsNullOrWhiteSpace(executablePath) ? "<unavailable>" : executablePath;
        var message =
            "A client requested access to FireBox capability APIs.\n\n" +
            $"Process: {processName}\n" +
            $"Path: {pathText}\n\n" +
            "Allow this client now?";

        var approved = User32.ShowYesNoMessageBox(title, message);
        if (!approved)
        {
            ServiceRuntimeLog.WriteInfo(null, "ConfigManager.ClientApproval", $"DENY process={processName}, path={pathText}");
            _configRepo.SetClientAccessDecisionAsync(
                processName,
                executablePath,
                isAllowed: false,
                deniedUntilUtc: DateTimeOffset.UtcNow.Add(_serviceOptions.ResolveAccessDenyCooldown())).GetAwaiter().GetResult();
            return false;
        }

        ServiceRuntimeLog.WriteInfo(null, "ConfigManager.ClientApproval", $"ALLOW process={processName}, path={pathText}");
        _configRepo.SetClientAccessDecisionAsync(
            processName,
            executablePath,
            isAllowed: true,
            deniedUntilUtc: null).GetAwaiter().GetResult();
        return true;
    }

    public void SetConnectionStreamState(long connectionId, bool hasActiveStream) =>
        _connections.SetStreamState(connectionId, hasActiveStream);

    private string NormalizeRequiredBaseUrl(string baseUrl) =>
        _baseUrlNormalizer.Normalize(baseUrl);

    private T Execute<T>(string operation, string details, Func<T> action)
    {
        try
        {
            ServiceRuntimeLog.WriteInfo(null, $"ConfigManager.{operation}", details);
            return action();
        }
        catch (Exception ex)
        {
            ServiceRuntimeLog.WriteError(null, $"ConfigManager.{operation}", ex, details);
            throw;
        }
    }

    private void Execute(string operation, string details, Action action)
    {
        Execute<object?>(operation, details, () =>
        {
            action();
            return null;
        });
    }
}

internal static class User32
{
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_DEFBUTTON2 = 0x00000100;
    private const uint MB_TOPMOST = 0x00040000;
    private const int IDYES = 6;

    public static bool ShowYesNoMessageBox(string title, string message)
    {
        var result = MessageBoxW(
            IntPtr.Zero,
            message,
            title,
            MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2 | MB_TOPMOST);
        return result == IDYES;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(
        IntPtr hWnd,
        string lpText,
        string lpCaption,
        uint uType);
}
