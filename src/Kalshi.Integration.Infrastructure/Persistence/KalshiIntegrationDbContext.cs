using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Represents the Entity Framework Core database context used by the publisher's persistence layer.
/// </summary>
public sealed class KalshiIntegrationDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KalshiIntegrationDbContext"/> class.
    /// </summary>
    /// <param name="options">The database-context options supplied by Entity Framework Core.</param>
    public KalshiIntegrationDbContext(DbContextOptions<KalshiIntegrationDbContext> options) : base(options)
    {
    }

    public DbSet<TradeIntentEntity> TradeIntents => Set<TradeIntentEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderEventEntity> OrderEvents => Set<OrderEventEntity>();
    public DbSet<PositionSnapshotEntity> PositionSnapshots => Set<PositionSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeIntentEntity>(entity =>
        {
            entity.ToTable("TradeIntents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Ticker).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Side).HasMaxLength(16).IsRequired();
            entity.Property(x => x.StrategyName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.LimitPrice).HasPrecision(10, 4);
        });

        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TradeIntentId);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<OrderEventEntity>(entity =>
        {
            entity.ToTable("OrderEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<PositionSnapshotEntity>(entity =>
        {
            entity.ToTable("PositionSnapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Ticker, x.Side }).IsUnique();
            entity.Property(x => x.Ticker).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Side).HasMaxLength(16).IsRequired();
            entity.Property(x => x.AveragePrice).HasPrecision(10, 4);
        });
    }
}
