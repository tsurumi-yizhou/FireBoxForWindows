using System.IO;
using Core.Com;
using Microsoft.Win32;

namespace Service.Com;

internal static class ComSelfRegistration
{
    private const string ClassesRoot = @"Software\Classes";

    // CLSID of the OLE Automation Universal Marshaler.
    private const string UniversalMarshalerClsid32 = "{00020424-0000-0000-C000-000000000046}";

    public static void RegisterPerUser()
    {
        var exePath = ResolveServerExecutablePath();
        var exeDir = Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("Unable to resolve executable directory.");
        var tlbPath = Path.Combine(exeDir, "FireBox.tlb");

        if (!File.Exists(tlbPath))
            throw new FileNotFoundException("Type library not found next to Service.exe. Build Service to generate FireBox.tlb.", tlbPath);

        RegisterTypeLib(tlbPath);

        RegisterLocalServer(FireBoxGuids.ControlClass, "FireBoxControl", exePath);
        RegisterLocalServer(FireBoxGuids.CapabilityClass, "FireBoxCapability", exePath);

        RegisterInterface(FireBoxGuids.ControlInterface, "IFireBoxControl");
        RegisterInterface(FireBoxGuids.CapabilityInterface, "IFireBoxCapability");
        RegisterInterface(FireBoxGuids.StreamCallbackInterface, "IFireBoxStreamCallback");
    }

    public static void UnregisterPerUser()
    {
        DeleteSubKeyTree($@"{ClassesRoot}\CLSID\{{{FireBoxGuids.ControlClass}}}");
        DeleteSubKeyTree($@"{ClassesRoot}\CLSID\{{{FireBoxGuids.CapabilityClass}}}");

        DeleteSubKeyTree($@"{ClassesRoot}\Interface\{{{FireBoxGuids.ControlInterface}}}");
        DeleteSubKeyTree($@"{ClassesRoot}\Interface\{{{FireBoxGuids.CapabilityInterface}}}");
        DeleteSubKeyTree($@"{ClassesRoot}\Interface\{{{FireBoxGuids.StreamCallbackInterface}}}");
        DeleteSubKeyTree($@"{ClassesRoot}\TypeLib\{{{FireBoxGuids.TypeLib}}}");
    }

    private static void RegisterLocalServer(string clsid, string displayName, string exePath)
    {
        using var clsidKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\CLSID\{{{clsid}}}", writable: true);
        if (clsidKey is null) throw new InvalidOperationException("Failed to create HKCU COM registration key.");

        clsidKey.SetValue(null, displayName, RegistryValueKind.String);

        using var localServer32Key = clsidKey.CreateSubKey("LocalServer32", writable: true);
        if (localServer32Key is null) throw new InvalidOperationException("Failed to create LocalServer32 key.");

        localServer32Key.SetValue(null, QuoteIfNeeded(exePath), RegistryValueKind.String);
    }

    private static void RegisterInterface(string iid, string interfaceName)
    {
        using var interfaceKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\Interface\{{{iid}}}", writable: true);
        if (interfaceKey is null) throw new InvalidOperationException("Failed to create Interface key.");

        interfaceKey.SetValue(null, interfaceName, RegistryValueKind.String);

        using var proxyStubClsid32Key = interfaceKey.CreateSubKey("ProxyStubClsid32", writable: true);
        if (proxyStubClsid32Key is null) throw new InvalidOperationException("Failed to create ProxyStubClsid32 key.");

        proxyStubClsid32Key.SetValue(null, UniversalMarshalerClsid32, RegistryValueKind.String);

        using var typeLibKey = interfaceKey.CreateSubKey("TypeLib", writable: true);
        if (typeLibKey is null) throw new InvalidOperationException("Failed to create TypeLib key.");

        typeLibKey.SetValue(null, $"{{{FireBoxGuids.TypeLib}}}", RegistryValueKind.String);
        typeLibKey.SetValue("Version", FireBoxGuids.TypeLibVersion, RegistryValueKind.String);
    }

    private static void RegisterTypeLib(string tlbPath)
    {
        using var typeLibKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\TypeLib\{{{FireBoxGuids.TypeLib}}}", writable: true);
        if (typeLibKey is null) throw new InvalidOperationException("Failed to create TypeLib key.");

        typeLibKey.SetValue(null, "FireBox Type Library", RegistryValueKind.String);

        using var versionKey = typeLibKey.CreateSubKey(FireBoxGuids.TypeLibVersion, writable: true);
        if (versionKey is null) throw new InvalidOperationException("Failed to create TypeLib version key.");

        versionKey.SetValue(null, "FireBox Type Library", RegistryValueKind.String);

        using var lcidKey = versionKey.CreateSubKey("0", writable: true);
        if (lcidKey is null) throw new InvalidOperationException("Failed to create TypeLib LCID key.");

        using var win64Key = lcidKey.CreateSubKey("win64", writable: true);
        if (win64Key is null) throw new InvalidOperationException("Failed to create TypeLib win64 key.");

        win64Key.SetValue(null, tlbPath, RegistryValueKind.String);
    }

    private static void DeleteSubKeyTree(string subKeyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort: ignore failures during cleanup.
        }
    }

    private static string QuoteIfNeeded(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (path.Contains(' ') && !(path.StartsWith('"') && path.EndsWith('"')))
            return $"\"{path}\"";
        return path;
    }

    private static string ResolveServerExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            File.Exists(processPath) &&
            string.Equals(Path.GetFileName(processPath), "Service.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // `dotnet run` hosts this app in dotnet.exe, so we must resolve the apphost executable explicitly.
        var appHostPath = Path.Combine(AppContext.BaseDirectory, "Service.exe");
        if (File.Exists(appHostPath))
            return appHostPath;

        var assemblyName = typeof(ComSelfRegistration).Assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var assemblyHostPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.exe");
            if (File.Exists(assemblyHostPath))
                return assemblyHostPath;
        }

        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            return processPath;

        throw new InvalidOperationException("Unable to resolve Service.exe path for COM registration.");
    }
}
