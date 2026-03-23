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

    public string Ping(string message) => $"Pong: {message}";

    public void Shutdown() => _lifetime.StopApplication();

    public string GetDailyStats(int year, int month, int day) =>
        Resolve<IFireBoxConfigManager>().GetDailyStats(year, month, day);

    public string GetMonthlyStats(int year, int month) =>
        Resolve<IFireBoxConfigManager>().GetMonthlyStats(year, month);

    public string ListProviders() =>
        Resolve<IFireBoxConfigManager>().ListProviders();

    public int AddProvider(string providerType, string name, string baseUrl, string apiKey) =>
        Resolve<IFireBoxConfigManager>().AddProvider(providerType, name, baseUrl, apiKey);

    public void UpdateProvider(int providerId, string name, string baseUrl, string apiKey, string enabledModelIdsJson, int isEnabled) =>
        Resolve<IFireBoxConfigManager>().UpdateProvider(providerId, name, baseUrl, apiKey, enabledModelIdsJson, isEnabled != 0);

    public void DeleteProvider(int providerId) =>
        Resolve<IFireBoxConfigManager>().DeleteProvider(providerId);

    public string FetchProviderModels(int providerId) =>
        Resolve<IFireBoxConfigManager>().FetchProviderModels(providerId);

    public string ListRoutes() =>
        Resolve<IFireBoxConfigManager>().ListRoutes();

    public int AddRoute(string virtualModelId, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Resolve<IFireBoxConfigManager>().AddRoute(virtualModelId, strategy, candidatesJson,
            reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask);

    public void UpdateRoute(int routeId, string virtualModelId, string strategy, string candidatesJson,
        int reasoning, int toolCalling, int inputFormatsMask, int outputFormatsMask) =>
        Resolve<IFireBoxConfigManager>().UpdateRoute(routeId, virtualModelId, strategy, candidatesJson,
            reasoning != 0, toolCalling != 0, inputFormatsMask, outputFormatsMask);

    public void DeleteRoute(int routeId) =>
        Resolve<IFireBoxConfigManager>().DeleteRoute(routeId);

    public string ListConnections() =>
        Resolve<IFireBoxConfigManager>().ListConnections();

    public string ListClientAccess() =>
        Resolve<IFireBoxConfigManager>().ListClientAccess();

    public void UpdateClientAccessAllowed(int accessId, int isAllowed) =>
        Resolve<IFireBoxConfigManager>().UpdateClientAccessAllowed(accessId, isAllowed != 0);
}
