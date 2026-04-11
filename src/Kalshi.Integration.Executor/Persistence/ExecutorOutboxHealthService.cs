using Kalshi.Integration.Contracts.Reliability;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Executor.Persistence;

/// <summary>
/// Summarizes executor outbox health for monitoring and readiness checks.
/// </summary>
public sealed class ExecutorOutboxHealthService(ExecutorDbContext dbContext)
{
    private readonly ExecutorDbContext _dbContext = dbContext;

    public async Task<OutboxHealthSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        IQueryable<Entities.ExecutorOutboxMessageEntity> pendingQuery = _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => x.Status == OutboxMessageStatus.Pending.ToString() || x.Status == OutboxMessageStatus.InFlight.ToString());

        long pendingCount = await pendingQuery.LongCountAsync(cancellationToken);
        long manualInterventionCount = await _dbContext.OutboxMessages
            .AsNoTracking()
            .LongCountAsync(x => x.Status == OutboxMessageStatus.ManualInterventionRequired.ToString(), cancellationToken);
        List<DateTimeOffset> pendingCreatedAt = await pendingQuery
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        DateTimeOffset? oldestPendingCreatedAt = pendingCreatedAt.Count == 0
            ? null
            : pendingCreatedAt.Min();

        return new OutboxHealthSnapshot(pendingCount, manualInterventionCount, oldestPendingCreatedAt);
    }
}
