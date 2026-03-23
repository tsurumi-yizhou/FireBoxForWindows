using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Com;

namespace Client;

/// <summary>
/// Manages the COM connection to the FireBox service's IFireBoxCapability interface.
/// Always connects via COM out-of-process. Service.exe must be running.
/// </summary>
public sealed class CapabilityConnection : IDisposable
{
    private IFireBoxCapability? _capability;
    private bool _disposed;

    public IFireBoxCapability Capability => _capability ?? throw new InvalidOperationException("Not connected.");

    public bool IsConnected => _capability is not null;

    public string? LastWarningMessage { get; private set; }

    public void Connect()
    {
        if (_capability is not null) return;

        var clsid = Guid.Parse(FireBoxGuids.CapabilityClass);
        var iid = Guid.Parse(FireBoxGuids.CapabilityInterface);

        var hr = RetryCreateInstance(ref clsid, ref iid, out var ppv);

        if (hr < 0)
            throw new InvalidOperationException(
                $"Failed to connect to FireBox Service (HRESULT 0x{hr:X8}). Ensure Service is running and COM is registered.");

        var cw = new StrategyBasedComWrappers();
        _capability = (IFireBoxCapability)cw.GetOrCreateObjectForComInstance(ppv, CreateObjectFlags.None);
        ReportClientIdentity();
    }

    private void ReportClientIdentity()
    {
        if (_capability is null)
            return;

        LastWarningMessage = null;

        try
        {
            var process = Environment.ProcessPath is { Length: > 0 }
                ? System.Diagnostics.Process.GetCurrentProcess()
                : null;

            var processId = Environment.ProcessId;
            var processName = process?.ProcessName ?? string.Empty;
            var executablePath = Environment.ProcessPath ?? string.Empty;
            _capability.Ping($"__firebox_identity__:{processId}|{processName}|{executablePath}");
        }
        catch (Exception ex)
        {
            LastWarningMessage = $"Client identity reporting failed: {ex.Message}";
            Trace.TraceWarning($"[CapabilityConnection] {LastWarningMessage}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capability = null;
    }

    private static int TryCreateInstance(ref Guid clsid, ref Guid iid, out IntPtr ppv) =>
        Ole32.CoCreateInstance(ref clsid, IntPtr.Zero, Ole32.CLSCTX_LOCAL_SERVER, ref iid, out ppv);

    private static int RetryCreateInstance(ref Guid clsid, ref Guid iid, out IntPtr ppv)
    {
        var hr = unchecked((int)0x80040154);
        ppv = IntPtr.Zero;

        for (var i = 0; i < 12; i++)
        {
            hr = TryCreateInstance(ref clsid, ref iid, out ppv);
            if (hr >= 0)
                return hr;

            Thread.Sleep(250);
        }

        return hr;
    }
}

file static class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 4;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out IntPtr ppv);
}
