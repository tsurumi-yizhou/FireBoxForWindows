using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Com;
using Core.Configuration;
using Core.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Service.Com;
using Service.Data;
using Service.Dispatch;
using Service.Providers;

if (ComArgs.HasArg(args, "/RegServer", "-RegServer"))
{
    ComSelfRegistration.RegisterPerUser();
    ServiceRuntimeLog.WriteInfo(null, "Program.RegServer", "Per-user COM registration completed.");
    return;
}

if (ComArgs.HasArg(args, "/UnregServer", "-UnregServer"))
{
    ComSelfRegistration.UnregisterPerUser();
    ServiceRuntimeLog.WriteInfo(null, "Program.UnregServer", "Per-user COM unregistration completed.");
    return;
}

StartupComRegistration.TryRefreshPerUserComRegistration();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
var serviceOptions = ServiceOptionsLoader.Load(builder.Configuration);

using var singleInstanceMutex = new Mutex(initiallyOwned: true, serviceOptions.SingleInstanceMutexName, out var isPrimaryInstance);
if (!isPrimaryInstance)
{
    return;
}

var efLoggerFactory = LoggerFactory.Create(_ => { });

builder.Logging.ClearProviders();
builder.Logging.AddDebug();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// --- EF Core SQLite ---
var dbPath = serviceOptions.ResolveDatabasePath();
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContextFactory<FireBoxDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}")
        .UseLoggerFactory(efLoggerFactory));

builder.Services.AddSingleton(serviceOptions);

// --- Data layer ---
builder.Services.AddSingleton<SecureKeyStore>();
builder.Services.AddSingleton<FireBoxConfigurationStore>();
builder.Services.AddSingleton<FireBoxConfigRepository>();
builder.Services.AddSingleton<FireBoxStatsRepository>();

// --- Provider layer ---
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ProviderBaseUrlNormalizer>();
builder.Services.AddSingleton<ProviderGatewayFactory>();
builder.Services.AddSingleton<ProviderModelFetcher>();

// --- Dispatch layer ---
builder.Services.AddSingleton<ConnectionStateHolder>();
builder.Services.AddSingleton<IFireBoxAiDispatcher, FireBoxAiDispatcher>();
builder.Services.AddSingleton<IFireBoxConfigManager, FireBoxConfigManager>();

// --- COM host ---
builder.Services.AddHostedService<ComServerHostedService>();

var host = builder.Build();

// --- EF Core: ensure DB created ---
using (var scope = host.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FireBoxDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

// --- Inject ServiceProvider into COM class ---
FireBoxCapabilityClass.ServiceProvider = host.Services;
FireBoxControlClass.ServiceProvider = host.Services;

host.Run();

/// <summary>
/// Registers both COM class factories on start and revokes them on stop.
/// The Host keeps the process alive indefinitely.
/// </summary>
file class ComServerHostedService(IHostApplicationLifetime lifetime, IServiceProvider serviceProvider) : IHostedService
{
    private uint _controlCookie;
    private uint _capabilityCookie;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cw = new StrategyBasedComWrappers();
        ServiceRuntimeLog.WriteInfo(serviceProvider, "ComServer.Start", "Registering COM class factories.");

        RegisterClass(
            FireBoxGuids.ControlClass,
            new ComClassFactory<FireBoxControlClass>("FireBoxControlClass", () => new FireBoxControlClass(lifetime), cw, serviceProvider),
            out _controlCookie);

        RegisterClass(
            FireBoxGuids.CapabilityClass,
            new ComClassFactory<FireBoxCapabilityClass>("FireBoxCapabilityClass", () => new FireBoxCapabilityClass(), cw, serviceProvider),
            out _capabilityCookie);

        Marshal.ThrowExceptionForHR(Ole32.CoResumeClassObjects());
        ServiceRuntimeLog.WriteInfo(serviceProvider, "ComServer.Start", $"COM classes resumed. controlCookie={_controlCookie}, capabilityCookie={_capabilityCookie}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ServiceRuntimeLog.WriteInfo(serviceProvider, "ComServer.Stop", $"Revoking COM classes. controlCookie={_controlCookie}, capabilityCookie={_capabilityCookie}");
        RevokeClass(ref _controlCookie);
        RevokeClass(ref _capabilityCookie);
        return Task.CompletedTask;
    }

    private void RegisterClass(string guidStr, object factory, out uint cookie)
    {
        var clsid = Guid.Parse(guidStr);
        var hr = Ole32.CoRegisterClassObject(
            ref clsid,
            factory,
            Ole32.CLSCTX_LOCAL_SERVER,
            Ole32.REGCLS_MULTIPLEUSE | Ole32.REGCLS_SUSPENDED,
            out cookie);
        if (hr < 0)
            ServiceRuntimeLog.WriteInfo(serviceProvider, "ComServer.RegisterClass", $"FAILED clsid={guidStr}, hr=0x{hr:X8}");
        else
            ServiceRuntimeLog.WriteInfo(serviceProvider, "ComServer.RegisterClass", $"OK clsid={guidStr}, cookie={cookie}");
        Marshal.ThrowExceptionForHR(hr);
    }

    private void RevokeClass(ref uint cookie)
    {
        if (cookie != 0)
        {
            Ole32.CoRevokeClassObject(cookie);
            cookie = 0;
        }
    }
}

/// <summary>
/// Generic IClassFactory that creates instances of a GeneratedComClass type.
/// </summary>
file class ComClassFactory<T>(string className, Func<T> factory, StrategyBasedComWrappers comWrappers, IServiceProvider serviceProvider) : IClassFactory
    where T : class
{
    public unsafe int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
    {
        ppvObject = nint.Zero;
        if (pUnkOuter != nint.Zero)
        {
            ServiceRuntimeLog.WriteInfo(serviceProvider, "ComClassFactory.CreateInstance", $"class={className}, rejected aggregation request for iid={riid}");
            return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
        }

        var obj = factory();
        var unk = comWrappers.GetOrCreateComInterfaceForObject(obj, CreateComInterfaceFlags.None);

        Guid iid = riid;
        var hr = Marshal.QueryInterface(unk, in iid, out ppvObject);
        Marshal.Release(unk);
        if (hr < 0)
            ServiceRuntimeLog.WriteInfo(serviceProvider, "ComClassFactory.CreateInstance", $"class={className}, QueryInterface failed iid={riid}, hr=0x{hr:X8}");
        return hr;
    }

    public int LockServer(bool fLock) => 0;
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
file interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);
    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

file static class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 4;
    public const uint REGCLS_MULTIPLEUSE = 1;
    public const uint REGCLS_SUSPENDED = 4;

    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoResumeClassObjects();
}

file static class ComArgs
{
    public static bool HasArg(string[] args, params string[] candidates)
    {
        foreach (var arg in args)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(arg, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}

file static class ServiceOptionsLoader
{
    public static FireBoxServiceOptions Load(IConfiguration configuration)
    {
        var options = new FireBoxServiceOptions();
        configuration.GetSection(FireBoxServiceOptions.SectionName).Bind(options);
        options.Validate();
        return options;
    }
}

file static class StartupComRegistration
{
    public static void TryRefreshPerUserComRegistration()
    {
        try
        {
            ComSelfRegistration.UnregisterPerUser();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"COM unregister skipped: {ex.Message}");
            ServiceRuntimeLog.WriteError(null, "Program.ComUnregister", ex, "COM unregister skipped.");
        }

        try
        {
            ComSelfRegistration.RegisterPerUser();
            ServiceRuntimeLog.WriteInfo(null, "Program.ComRegister", "Per-user COM registration refreshed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"COM registration refresh failed: {ex.Message}");
            ServiceRuntimeLog.WriteError(null, "Program.ComRegister", ex, "COM registration refresh failed.");
        }
    }
}
