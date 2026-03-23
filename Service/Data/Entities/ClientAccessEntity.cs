using Microsoft.EntityFrameworkCore;

namespace Service.Data.Entities;

[Index(nameof(ProcessName), nameof(ExecutablePath))]
public sealed class ClientAccessEntity
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsAllowed { get; set; } = false;
    public DateTimeOffset? DeniedUntilUtc { get; set; }
}
