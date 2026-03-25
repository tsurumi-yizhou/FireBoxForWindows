using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Configuration;
using Core.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.Com;

[GeneratedComClass]
[Guid(FireBoxGuids.ControlClass)]
public partial class FireBoxControlClass : IFireBoxControl
{
    private readonly IHostApplicationLifetime _lifetime;

    public static IServiceProvider? ServiceProvider { get; set; }

    public FireBoxControlClass(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    private T Resolve<T>() where T : class =>
        (ServiceProvider ?? throw new InvalidOperationException("Service not initialized."))
            .GetRequiredService<T>();

    private IFireBoxConfigManager ResolveConfig() => Resolve<IFireBoxConfigManager>();

    private FireBoxServiceOptions ResolveOptions() => Resolve<FireBoxServiceOptions>();

    private T Execute<T>(string operation, string? details, Func<IFireBoxConfigManager, T> action)
    {
        try
        {
            EnsureAuthorized();
            ServiceRuntimeLog.WriteInfo(ServiceProvider, $"Control.{operation}", details ?? string.Empty);
            return action(ResolveConfig());
        }
        catch (Exception ex)
        {
            ServiceRuntimeLog.WriteError(ServiceProvider, $"Control.{operation}", ex, details);
            throw;
        }
    }

    private void Execute(string operation, string? details, Action<IFireBoxConfigManager> action)
    {
        Execute<object?>(operation, details, manager =>
        {
            action(manager);
            return null;
        });
    }

    public string Ping(string message) =>
        Execute("Ping", $"message={message}", _ => $"Pong: {message}");

    public void Shutdown()
    {
        Execute("Shutdown", "Shutdown requested.", _ => _lifetime.StopApplication());
    }

    public int GetVersionCode() =>
        Execute("GetVersionCode", null, manager => manager.GetVersionCode());

    public string GetDailyStats(int year, int month, int day) =>
        Execute("GetDailyStats", $"date={year:D4}-{month:D2}-{day:D2}", manager => manager.GetDailyStats(year, month, day));

    public string GetMonthlyStats(int year, int month) =>
        Execute("GetMonthlyStats", $"month={year:D4}-{month:D2}", manager => manager.GetMonthlyStats(year, month));

    public string ListProviders() =>
        Execute("ListProviders", null, manager => manager.ListProviders());

    public int AddProvider(string providerType, string name, string baseUrl, string apiKey) =>
        Execute("AddProvider", $"providerType={providerType}, name={name}, baseUrl={(string.IsNullOrWhiteSpace(baseUrl) ? "<empty>" : baseUrl)}", manager =>
            manager.AddProvider(providerType, name, baseUrl, apiKey));

    public void UpdateProvider(int providerId, string name, string baseUrl, string? apiKey, int apiKeyProvided, string enabledModelIdsJson) =>
        Execute("UpdateProvider", $"providerId={providerId}, name={name}, baseUrl={baseUrl}, apiKeyProvided={apiKeyProvided != 0}, enabledModelIdsJsonLength={enabledModelIdsJson?.Length ?? 0}", manager =>
            manager.UpdateProvider(providerId, name, baseUrl, apiKey, apiKeyProvided != 0, enabledModelIdsJson ?? string.Empty));

    public void DeleteProvider(int providerId) =>
        Execute("DeleteProvider", $"providerId={providerId}", manager => manager.DeleteProvider(providerId));

    public string FetchProviderModels(int providerId) =>
        Execute("FetchProviderModels", $"providerId={providerId}", manager => manager.FetchProviderModels(providerId));

    public string ListRoutes() =>
        Execute("ListRoutes", null, manager => manager.ListRoutes());

    public int AddRoute(string routeId, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("AddRoute", $"routeId={routeId}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", manager =>
            manager.AddRoute(routeId, strategy, candidatesJson ?? "[]", reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask));

    public void UpdateRoute(int routeId, string routeIdValue, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("UpdateRoute", $"routeId={routeId}, routeIdValue={routeIdValue}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", manager =>
            manager.UpdateRoute(routeId, routeIdValue, strategy, candidatesJson ?? "[]", reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask));

    public void DeleteRoute(int routeId) =>
        Execute("DeleteRoute", $"routeId={routeId}", manager => manager.DeleteRoute(routeId));

    public string ListConnections() =>
        Execute("ListConnections", null, manager => manager.ListConnections());

    public string ListClientAccess() =>
        Execute("ListClientAccess", null, manager => manager.ListClientAccess());

    public void UpdateClientAccessAllowed(int accessId, int isAllowed) =>
        Execute("UpdateClientAccessAllowed", $"accessId={accessId}, isAllowed={isAllowed}", manager => manager.UpdateClientAccessAllowed(accessId, isAllowed != 0));

    private void EnsureAuthorized()
    {
        var caller = IdentifyCaller();
        if (caller is null)
            throw new UnauthorizedAccessException("Unable to identify calling process. Control access denied.");

        var (_, processName, executablePath) = caller.Value;
        if (!ResolveOptions().IsControlExecutableAllowed(executablePath))
        {
            throw new UnauthorizedAccessException(
                $"Control access denied for '{processName}' ({executablePath}). An administrator must use an allowed management client.");
        }
    }

    private static (int Pid, string ProcessName, string ExePath)? IdentifyCaller()
    {
        try
        {
            var pid = GetCallerPid();
            if (pid <= 0)
                return null;

            using var proc = Process.GetProcessById(pid);
            var processName = proc.ProcessName;
            string executablePath;
            try
            {
                executablePath = proc.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                executablePath = string.Empty;
            }

            return (pid, processName, executablePath);
        }
        catch
        {
            return null;
        }
    }

    private static int GetCallerPid()
    {
        var hr = Ole32_Rpc.TryGetCallerPidViaLocalRpc(out var pid);
        if (hr >= 0 && pid > 0)
            return pid;

        return 0;
    }
}
