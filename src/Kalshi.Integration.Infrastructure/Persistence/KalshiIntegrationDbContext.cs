using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Represents the Entity Framework Core database context used by the publisher's persistence layer.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="KalshiIntegrationDbContext"/> class.
/// </remarks>
/// <param name="options">The database-context options supplied by Entity Framework Core.</param>
public sealed class KalshiIntegrationDbContext(DbContextOptions<KalshiIntegrationDbContext> options) : DbContext(options)
{
    public DbSet<TradeIntentEntity> TradeIntents => Set<TradeIntentEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderEventEntity> OrderEvents => Set<OrderEventEntity>();
    public DbSet<OrderLifecycleEventEntity> OrderLifecycleEvents => Set<OrderLifecycleEventEntity>();
    public DbSet<ResultEventEntity> ResultEvents => Set<ResultEventEntity>();
    public DbSet<PublisherOutboxMessageEntity> PublisherOutboxMessages => Set<PublisherOutboxMessageEntity>();
    public DbSet<PublisherOutboxAttemptEntity> PublisherOutboxAttempts => Set<PublisherOutboxAttemptEntity>();
    public DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();
    public DbSet<OperationalIssueEntity> OperationalIssues => Set<OperationalIssueEntity>();
    public DbSet<PositionSnapshotEntity> PositionSnapshots => Set<PositionSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeIntentEntity>(entity =>
        {
            entity.ToTable("TradeIntents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Ticker).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Side).HasMaxLength(16);
            entity.Property(x => x.StrategyName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActionType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.OriginService).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DecisionReason).HasMaxLength(512).IsRequired();
            entity.Property(x => x.CommandSchemaVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetPositionTicker).HasMaxLength(64);
            entity.Property(x => x.TargetPositionSide).HasMaxLength(16);
            entity.Property(x => x.TargetClientOrderId).HasMaxLength(128);
            entity.Property(x => x.TargetExternalOrderId).HasMaxLength(128);
            entity.Property(x => x.LimitPrice).HasPrecision(10, 4);
        });

        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TradeIntentId).IsUnique();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PublishStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastResultStatus).HasMaxLength(64);
            entity.Property(x => x.LastResultMessage).HasMaxLength(1024);
            entity.Property(x => x.ExternalOrderId).HasMaxLength(128);
            entity.Property(x => x.ClientOrderId).HasMaxLength(128);
            entity.HasIndex(x => x.ExternalOrderId);
            entity.HasIndex(x => x.ClientOrderId).IsUnique();
        });

        modelBuilder.Entity<OrderEventEntity>(entity =>
        {
            entity.ToTable("OrderEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<OrderLifecycleEventEntity>(entity =>
        {
            entity.ToTable("OrderLifecycleEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.Property(x => x.Stage).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(1024);
        });

        modelBuilder.Entity<ResultEventEntity>(entity =>
        {
            entity.ToTable("ResultEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.AppliedAt);
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1024);
        });

        modelBuilder.Entity<PublisherOutboxMessageEntity>(entity =>
        {
            entity.ToTable("PublisherOutboxMessages");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => new { x.AggregateType, x.AggregateId });
            entity.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProcessorId).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(1024);
            entity.Property(x => x.LastFailureKind).HasMaxLength(128);
        });

        modelBuilder.Entity<PublisherOutboxAttemptEntity>(entity =>
        {
            entity.ToTable("PublisherOutboxAttempts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MessageId, x.AttemptNumber }).IsUnique();
            entity.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FailureKind).HasMaxLength(128);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1024);
        });

        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAt);
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.ResourceId).HasMaxLength(128);
            entity.Property(x => x.Details).HasMaxLength(2048).IsRequired();
        });

        modelBuilder.Entity<IdempotencyRecordEntity>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
            entity.Property(x => x.Scope).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ResponseBody).HasColumnType("TEXT").IsRequired();
        });

        modelBuilder.Entity<OperationalIssueEntity>(entity =>
        {
            entity.ToTable("OperationalIssues");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAt);
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(2048);
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
