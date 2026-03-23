using Microsoft.EntityFrameworkCore;

namespace Service.Data.Entities;

[Index(nameof(Date))]
[Index(nameof(ProviderId), nameof(ModelId), nameof(Date))]
public sealed class DailyUsageEntity
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int ProviderId { get; set; }
    public string ProviderType { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}
