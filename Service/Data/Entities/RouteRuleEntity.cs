namespace Service.Data.Entities;

public sealed class RouteRuleEntity
{
    public int Id { get; set; }
    public string VirtualModelId { get; set; } = string.Empty;
    public string Strategy { get; set; } = "Ordered"; // "Ordered" or "Random"
    public string CandidatesJson { get; set; } = "[]"; // JSON: [{ProviderId, ModelId}]
    public bool Reasoning { get; set; }
    public bool ToolCalling { get; set; }
    public int InputFormatsMask { get; set; }  // bitmask: 1=Image, 2=Video, 4=Audio
    public int OutputFormatsMask { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
