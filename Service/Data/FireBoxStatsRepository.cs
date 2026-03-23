using Microsoft.EntityFrameworkCore;
using Service.Data.Entities;

namespace Service.Data;

public sealed class FireBoxStatsRepository
{
    private readonly IDbContextFactory<FireBoxDbContext> _dbFactory;

    public FireBoxStatsRepository(IDbContextFactory<FireBoxDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task RecordUsageAsync(int providerId, string providerType, string modelId, long promptTokens, long completionTokens, decimal estimatedCostUsd)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var entity = await db.DailyUsage.FirstOrDefaultAsync(u =>
            u.Date == today && u.ProviderId == providerId && u.ModelId == modelId);

        if (entity is not null)
        {
            entity.RequestCount++;
            entity.PromptTokens += promptTokens;
            entity.CompletionTokens += completionTokens;
            entity.TotalTokens += promptTokens + completionTokens;
            entity.EstimatedCostUsd += estimatedCostUsd;
        }
        else
        {
            db.DailyUsage.Add(new DailyUsageEntity
            {
                Date = today,
                ProviderId = providerId,
                ProviderType = providerType,
                ModelId = modelId,
                RequestCount = 1,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens,
                EstimatedCostUsd = estimatedCostUsd,
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<DailyStatsDto> GetDailyStatsAsync(DateOnly date)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.DailyUsage.Where(u => u.Date == date).ToListAsync();
        return Aggregate(rows);
    }

    public async Task<DailyStatsDto> GetMonthlyStatsAsync(int year, int month)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1);
        var rows = await db.DailyUsage
            .Where(u => u.Date >= startDate && u.Date < endDate)
            .ToListAsync();
        return Aggregate(rows);
    }

    private static DailyStatsDto Aggregate(List<DailyUsageEntity> rows) => new(
        RequestCount: rows.Sum(r => r.RequestCount),
        PromptTokens: rows.Sum(r => r.PromptTokens),
        CompletionTokens: rows.Sum(r => r.CompletionTokens),
        TotalTokens: rows.Sum(r => r.TotalTokens),
        EstimatedCostUsd: rows.Sum(r => r.EstimatedCostUsd));
}

public sealed record DailyStatsDto(
    long RequestCount,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    decimal EstimatedCostUsd);
