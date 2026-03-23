using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Com;

namespace App.Services;

/// <summary>
/// Manages the connection to the FireBox background service via COM.
/// Service.exe must be running (either manually or auto-launched via COM registration).
/// </summary>
public sealed class ControlConnection : IDisposable
{
    private IFireBoxControl? _control;
    private bool _disposed;

    public IFireBoxControl Control => _control ?? throw new InvalidOperationException("Not connected.");

    public bool IsConnected => _control is not null;

    public void Connect()
    {
        if (_control is not null) return;

        var clsid = Guid.Parse(FireBoxGuids.ControlClass);
        var iid = Guid.Parse(FireBoxGuids.ControlInterface);

        var hr = Ole32.CoCreateInstance(ref clsid, nint.Zero, Ole32.CLSCTX_LOCAL_SERVER, ref iid, out var ppv);
        if (hr < 0)
            throw new InvalidOperationException(
                $"Failed to connect to FireBox Service (HRESULT 0x{hr:X8}). Make sure Service.exe is running with /RegServer first.");

        var cw = new StrategyBasedComWrappers();
        _control = (IFireBoxControl)cw.GetOrCreateObjectForComInstance(ppv, CreateObjectFlags.None);

        try
        {
            _ = _control.Ping("__firebox_control_connect__");
        }
        catch (Exception ex)
        {
            _control = null;
            throw new InvalidOperationException("Connected to COM class, but Service control channel is not responding.", ex);
        }
    }

    public string Ping(string message) => Control.Ping(message);

    public void Shutdown()
    {
        _control?.Shutdown();
        _control = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_control is not null)
        {
            try { Shutdown(); } catch (Exception ex) { Debug.WriteLine($"[ControlConnection] Shutdown during dispose failed: {ex.Message}"); }
            _control = null;
        }
    }
}

file static class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 4;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out nint ppv);
}
