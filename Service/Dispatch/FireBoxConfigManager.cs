using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Configuration;
using Core.Dispatch;
using Service.Data;
using Service.Data.Entities;
using Service.Providers;

namespace Service.Dispatch;

public sealed class FireBoxConfigManager : IFireBoxConfigManager
{
    private readonly FireBoxConfigRepository _configRepo;
    private readonly FireBoxStatsRepository _statsRepo;
    private readonly ProviderModelFetcher _modelFetcher;
    private readonly ConnectionStateHolder _connections;
    private readonly FireBoxServiceOptions _serviceOptions;

    public FireBoxConfigManager(
        FireBoxConfigRepository configRepo,
        FireBoxStatsRepository statsRepo,
        ProviderModelFetcher modelFetcher,
        ConnectionStateHolder connections,
        FireBoxServiceOptions serviceOptions)
    {
        _configRepo = configRepo;
        _statsRepo = statsRepo;
        _modelFetcher = modelFetcher;
        _connections = connections;
        _serviceOptions = serviceOptions;
    }

    public string ListProviders()
    {
        var providers = _configRepo.ListProvidersAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(providers.Select(p => new
        {
            p.Id, p.ProviderType, p.Name, p.BaseUrl,
            p.IsEnabled,
            EnabledModelIds = _configRepo.GetEnabledModelIds(p),
            p.CreatedAt, p.UpdatedAt,
        }));
    }

    public int AddProvider(string providerType, string name, string baseUrl, string apiKey) =>
        _configRepo.AddProviderAsync(providerType, name, baseUrl, apiKey).GetAwaiter().GetResult();

    public void UpdateProvider(int id, string name, string baseUrl, string apiKey, string enabledModelIdsJson, bool isEnabled)
    {
        var enabledModelIds = string.IsNullOrEmpty(enabledModelIdsJson)
            ? null
            : JsonSerializer.Deserialize<List<string>>(enabledModelIdsJson);
        _configRepo.UpdateProviderAsync(id,
            string.IsNullOrEmpty(name) ? null : name,
            string.IsNullOrEmpty(baseUrl) ? null : baseUrl,
            string.IsNullOrEmpty(apiKey) ? null : apiKey,
            enabledModelIds,
            isEnabled).GetAwaiter().GetResult();
    }

    public void DeleteProvider(int id) =>
        _configRepo.DeleteProviderAsync(id).GetAwaiter().GetResult();

    public string FetchProviderModels(int providerId)
    {
        var provider = _configRepo.GetProviderAsync(providerId).GetAwaiter().GetResult()
            ?? throw new KeyNotFoundException($"Provider {providerId} not found.");
        var models = _modelFetcher.FetchModelsAsync(provider.ProviderType, provider.BaseUrl, provider.EncryptedApiKey)
            .GetAwaiter().GetResult();
        return JsonSerializer.Serialize(models);
    }

    public string ListRoutes()
    {
        var routes = _configRepo.ListRoutesAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(routes.Select(r => new
        {
            r.Id, r.VirtualModelId, r.Strategy,
            Candidates = _configRepo.GetCandidates(r),
            r.Reasoning, r.ToolCalling,
            r.InputFormatsMask, r.OutputFormatsMask,
            r.CreatedAt, r.UpdatedAt,
        }));
    }

    public int AddRoute(string virtualModelId, string strategy, string candidatesJson,
        bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        _configRepo.AddRouteAsync(new RouteRuleEntity
        {
            VirtualModelId = virtualModelId,
            Strategy = strategy,
            CandidatesJson = candidatesJson,
            Reasoning = reasoning,
            ToolCalling = toolCalling,
            InputFormatsMask = inputFormatsMask,
            OutputFormatsMask = outputFormatsMask,
        }).GetAwaiter().GetResult();

    public void UpdateRoute(int id, string virtualModelId, string strategy, string candidatesJson,
        bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        _configRepo.UpdateRouteAsync(new RouteRuleEntity
        {
            Id = id,
            VirtualModelId = virtualModelId,
            Strategy = strategy,
            CandidatesJson = candidatesJson,
            Reasoning = reasoning,
            ToolCalling = toolCalling,
            InputFormatsMask = inputFormatsMask,
            OutputFormatsMask = outputFormatsMask,
        }).GetAwaiter().GetResult();

    public void DeleteRoute(int id) =>
        _configRepo.DeleteRouteAsync(id).GetAwaiter().GetResult();

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
        _configRepo.UpdateClientAccessAllowedAsync(accessId, isAllowed).GetAwaiter().GetResult();

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
            _configRepo.SetClientAccessDecisionAsync(
                processName,
                executablePath,
                isAllowed: false,
                deniedUntilUtc: DateTimeOffset.UtcNow.Add(_serviceOptions.ResolveAccessDenyCooldown())).GetAwaiter().GetResult();
            return false;
        }

        _configRepo.SetClientAccessDecisionAsync(
            processName,
            executablePath,
            isAllowed: true,
            deniedUntilUtc: null).GetAwaiter().GetResult();
        return true;
    }

    public void SetConnectionStreamState(long connectionId, bool hasActiveStream) =>
        _connections.SetStreamState(connectionId, hasActiveStream);
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
