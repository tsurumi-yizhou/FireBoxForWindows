using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
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

    private T Execute<T>(string operation, string? details, Func<IFireBoxConfigManager, T> action)
    {
        try
        {
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

    public string Ping(string message)
    {
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Control.Ping", $"message={message}");
        return $"Pong: {message}";
    }

    public void Shutdown()
    {
        ServiceRuntimeLog.WriteInfo(ServiceProvider, "Control.Shutdown", "Shutdown requested.");
        _lifetime.StopApplication();
    }

    public string GetDailyStats(int year, int month, int day) =>
        Execute("GetDailyStats", $"date={year:D4}-{month:D2}-{day:D2}", manager => manager.GetDailyStats(year, month, day));

    public string GetMonthlyStats(int year, int month) =>
        Execute("GetMonthlyStats", $"month={year:D4}-{month:D2}", manager => manager.GetMonthlyStats(year, month));

    public string ListProviders() =>
        Execute("ListProviders", null, manager => manager.ListProviders());

    public int AddProvider(string providerType, string name, string baseUrl, string apiKey) =>
        Execute("AddProvider", $"providerType={providerType}, name={name}, baseUrl={(string.IsNullOrWhiteSpace(baseUrl) ? "<default>" : baseUrl)}", manager =>
            manager.AddProvider(providerType, name, baseUrl, apiKey));

    public void UpdateProvider(int providerId, string name, string baseUrl, string apiKey, string enabledModelIdsJson, int isEnabled) =>
        Execute("UpdateProvider", $"providerId={providerId}, name={name}, baseUrl={baseUrl}, apiKeyProvided={!string.IsNullOrWhiteSpace(apiKey)}, enabledModelIdsJsonLength={enabledModelIdsJson?.Length ?? 0}, isEnabled={isEnabled}", manager =>
            manager.UpdateProvider(providerId, name, baseUrl, apiKey, enabledModelIdsJson ?? string.Empty, isEnabled != 0));

    public void DeleteProvider(int providerId) =>
        Execute("DeleteProvider", $"providerId={providerId}", manager => manager.DeleteProvider(providerId));

    public string FetchProviderModels(int providerId) =>
        Execute("FetchProviderModels", $"providerId={providerId}", manager => manager.FetchProviderModels(providerId));

    public string ListRoutes() =>
        Execute("ListRoutes", null, manager => manager.ListRoutes());

    public int AddRoute(string virtualModelId, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("AddRoute", $"virtualModelId={virtualModelId}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", manager =>
            manager.AddRoute(virtualModelId, strategy, candidatesJson ?? "[]", reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask));

    public void UpdateRoute(int routeId, string virtualModelId, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Execute("UpdateRoute", $"routeId={routeId}, virtualModelId={virtualModelId}, strategy={strategy}, candidatesJsonLength={candidatesJson?.Length ?? 0}, reasoning={reasoning}, toolCalling={toolCalling}, inputFormatsMask={inputFormatsMask}, outputFormatsMask={outputFormatsMask}", manager =>
            manager.UpdateRoute(routeId, virtualModelId, strategy, candidatesJson ?? "[]", reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask));

    public void DeleteRoute(int routeId) =>
        Execute("DeleteRoute", $"routeId={routeId}", manager => manager.DeleteRoute(routeId));

    public string ListConnections() =>
        Execute("ListConnections", null, manager => manager.ListConnections());

    public string ListClientAccess() =>
        Execute("ListClientAccess", null, manager => manager.ListClientAccess());

    public void UpdateClientAccessAllowed(int accessId, int isAllowed) =>
        Execute("UpdateClientAccessAllowed", $"accessId={accessId}, isAllowed={isAllowed}", manager => manager.UpdateClientAccessAllowed(accessId, isAllowed != 0));
}
