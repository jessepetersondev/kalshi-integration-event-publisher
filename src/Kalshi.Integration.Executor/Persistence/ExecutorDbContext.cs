using Kalshi.Integration.Executor.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Executor.Persistence;

public sealed class ExecutorDbContext(DbContextOptions<ExecutorDbContext> options) : DbContext(options)
{
    public DbSet<ExecutorInboundMessageEntity> InboundMessages => Set<ExecutorInboundMessageEntity>();
    public DbSet<ExecutionRecordEntity> ExecutionRecords => Set<ExecutionRecordEntity>();
    public DbSet<ExternalOrderMappingEntity> ExternalOrderMappings => Set<ExternalOrderMappingEntity>();
    public DbSet<ExecutorOutboxMessageEntity> OutboxMessages => Set<ExecutorOutboxMessageEntity>();
    public DbSet<ExecutorOutboxAttemptEntity> OutboxAttempts => Set<ExecutorOutboxAttemptEntity>();
    public DbSet<ExecutorOperationalIssueEntity> OperationalIssues => Set<ExecutorOperationalIssueEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutorInboundMessageEntity>(entity =>
        {
            entity.ToTable("ExecutorInboundMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1024);
        });

        modelBuilder.Entity<ExecutionRecordEntity>(entity =>
        {
            entity.ToTable("ExecutionRecords");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublisherOrderId).IsUnique();
            entity.HasIndex(x => x.ClientOrderId).IsUnique();
            entity.HasIndex(x => x.ExternalOrderId).IsUnique();
            entity.Property(x => x.Ticker).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ActionType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Side).HasMaxLength(16);
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ClientOrderId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ExternalOrderId).HasMaxLength(128);
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LeaseOwner).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(1024);
            entity.Property(x => x.LastResultEventName).HasMaxLength(128);
            entity.Property(x => x.LimitPrice).HasPrecision(10, 4);
        });

        modelBuilder.Entity<ExternalOrderMappingEntity>(entity =>
        {
            entity.ToTable("ExternalOrderMappings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PublisherOrderId).IsUnique();
            entity.HasIndex(x => x.ClientOrderId).IsUnique();
            entity.HasIndex(x => x.ExternalOrderId).IsUnique();
            entity.Property(x => x.ClientOrderId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ExternalOrderId).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<ExecutorOutboxMessageEntity>(entity =>
        {
            entity.ToTable("ExecutorOutboxMessages");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => x.ExecutionRecordId);
            entity.Property(x => x.MessageType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProcessorId).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(1024);
            entity.Property(x => x.LastFailureKind).HasMaxLength(128);
        });

        modelBuilder.Entity<ExecutorOutboxAttemptEntity>(entity =>
        {
            entity.ToTable("ExecutorOutboxAttempts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MessageId, x.AttemptNumber }).IsUnique();
            entity.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FailureKind).HasMaxLength(128);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1024);
        });

        modelBuilder.Entity<ExecutorOperationalIssueEntity>(entity =>
        {
            entity.ToTable("ExecutorOperationalIssues");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAt);
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(2048);
        });
    }
}
