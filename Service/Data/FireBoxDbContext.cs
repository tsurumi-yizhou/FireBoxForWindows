using Microsoft.EntityFrameworkCore;
using Service.Data.Entities;

namespace Service.Data;

public sealed class FireBoxDbContext : DbContext
{
    public DbSet<DailyUsageEntity> DailyUsage => Set<DailyUsageEntity>();
    public DbSet<ClientAccessEntity> ClientAccess => Set<ClientAccessEntity>();

    public FireBoxDbContext(DbContextOptions<FireBoxDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyUsageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderType).HasMaxLength(32);
            e.Property(x => x.ModelId).HasMaxLength(256);
            e.Property(x => x.EstimatedCostUsd).HasPrecision(18, 8);
        });

        modelBuilder.Entity<ClientAccessEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProcessName).HasMaxLength(256);
            e.Property(x => x.ExecutablePath).HasMaxLength(1024);
        });
    }
}
