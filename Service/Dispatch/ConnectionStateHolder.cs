using System.Collections.Concurrent;

namespace Service.Dispatch;

public sealed class ConnectionStateHolder
{
    private readonly ConcurrentDictionary<long, ConnectionInfo> _connections = new();
    private long _nextConnectionId;

    public long RegisterConnection(int processId, string processName, string executablePath)
    {
        var id = Interlocked.Increment(ref _nextConnectionId);
        _connections[id] = new ConnectionInfo(id, processId, processName, executablePath, DateTimeOffset.UtcNow);
        return id;
    }

    public void UnregisterConnection(long connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public void IncrementRequestCount(long connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var info))
            Interlocked.Increment(ref info.RequestCount);
    }

    public void SetStreamState(long connectionId, bool hasActiveStream)
    {
        if (_connections.TryGetValue(connectionId, out var info))
            info.HasActiveStream = hasActiveStream;
    }

    public List<ConnectionInfo> GetActiveConnections() =>
        _connections.Values.OrderByDescending(c => c.ConnectedAt).ToList();

    public int ActiveCount => _connections.Count;
}

public sealed class ConnectionInfo
{
    public long ConnectionId { get; }
    public int ProcessId { get; }
    public string ProcessName { get; }
    public string ExecutablePath { get; }
    public DateTimeOffset ConnectedAt { get; }
    public long RequestCount;
    public bool HasActiveStream;

    public ConnectionInfo(long connectionId, int processId, string processName, string executablePath, DateTimeOffset connectedAt)
    {
        ConnectionId = connectionId;
        ProcessId = processId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        ConnectedAt = connectedAt;
    }
}
