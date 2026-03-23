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
    return;
}

if (ComArgs.HasArg(args, "/UnregServer", "-UnregServer"))
{
    ComSelfRegistration.UnregisterPerUser();
    return;
}

// In packaged (MSIX) deployments the manifest declares all COM registrations;
// self-registration is only needed in unpackaged (Debug) mode.
if (!PackageIdentityHelper.IsRunningAsPackaged())
{
    try
    {
        ComSelfRegistration.UnregisterPerUser();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"COM unregister skipped: {ex.Message}");
    }

    try
    {
        ComSelfRegistration.RegisterPerUser();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"COM registration refresh failed: {ex.Message}");
    }
}

var builder = Host.CreateApplicationBuilder(args);
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
file class ComServerHostedService(IHostApplicationLifetime lifetime) : IHostedService
{
    private uint _controlCookie;
    private uint _capabilityCookie;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cw = new StrategyBasedComWrappers();

        RegisterClass(
            FireBoxGuids.ControlClass,
            new ComClassFactory<FireBoxControlClass>(() => new FireBoxControlClass(lifetime), cw),
            out _controlCookie);

        RegisterClass(
            FireBoxGuids.CapabilityClass,
            new ComClassFactory<FireBoxCapabilityClass>(() => new FireBoxCapabilityClass(), cw),
            out _capabilityCookie);

        Marshal.ThrowExceptionForHR(Ole32.CoResumeClassObjects());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RevokeClass(ref _controlCookie);
        RevokeClass(ref _capabilityCookie);
        return Task.CompletedTask;
    }

    private static void RegisterClass(string guidStr, object factory, out uint cookie)
    {
        var clsid = Guid.Parse(guidStr);
        var hr = Ole32.CoRegisterClassObject(
            ref clsid,
            factory,
            Ole32.CLSCTX_LOCAL_SERVER,
            Ole32.REGCLS_MULTIPLEUSE | Ole32.REGCLS_SUSPENDED,
            out cookie);
        Marshal.ThrowExceptionForHR(hr);
    }

    private static void RevokeClass(ref uint cookie)
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
file class ComClassFactory<T>(Func<T> factory, StrategyBasedComWrappers comWrappers) : IClassFactory
    where T : class
{
    public unsafe int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
    {
        ppvObject = nint.Zero;
        if (pUnkOuter != nint.Zero)
            return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

        var obj = factory();
        var unk = comWrappers.GetOrCreateComInterfaceForObject(obj, CreateComInterfaceFlags.None);

        Guid iid = riid;
        var hr = Marshal.QueryInterface(unk, in iid, out ppvObject);
        Marshal.Release(unk);
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

file static class PackageIdentityHelper
{
    public static bool IsRunningAsPackaged()
    {
        int len = 0;
        // Returns APPMODEL_ERROR_NO_PACKAGE (15700) when the process has no package identity.
        return GetCurrentPackageFullName(ref len, null) != 15700;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
