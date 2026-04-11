using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Persists audit records in the relational publisher store.
/// </summary>
public sealed class EfAuditRecordStore(IDbContextFactory<KalshiIntegrationDbContext> dbContextFactory) : IAuditRecordStore
{
    private readonly IDbContextFactory<KalshiIntegrationDbContext> _dbContextFactory = dbContextFactory;

    public async Task AddAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.AuditRecords.Add(new AuditRecordEntity
        {
            Id = auditRecord.Id,
            Category = auditRecord.Category,
            Action = auditRecord.Action,
            Outcome = auditRecord.Outcome,
            CorrelationId = auditRecord.CorrelationId,
            IdempotencyKey = auditRecord.IdempotencyKey,
            ResourceId = auditRecord.ResourceId,
            Details = auditRecord.Details,
            OccurredAt = auditRecord.OccurredAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> GetRecentAsync(string? category = null, int hours = 24, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Abs(hours));
        IQueryable<AuditRecordEntity> query = dbContext.AuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category == category);
        }

        List<AuditRecordEntity> entities = await query.ToListAsync(cancellationToken);
        return entities
            .Where(x => x.OccurredAt >= cutoff)
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new AuditRecord(x.Id, x.Category, x.Action, x.Outcome, x.CorrelationId, x.IdempotencyKey, x.ResourceId, x.Details, x.OccurredAt))
            .ToArray();
    }
}
