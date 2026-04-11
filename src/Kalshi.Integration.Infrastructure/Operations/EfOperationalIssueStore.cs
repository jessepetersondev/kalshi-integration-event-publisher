using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Persists operational issues in the relational publisher store.
/// </summary>
public sealed class EfOperationalIssueStore(IDbContextFactory<KalshiIntegrationDbContext> dbContextFactory) : IOperationalIssueStore
{
    private readonly IDbContextFactory<KalshiIntegrationDbContext> _dbContextFactory = dbContextFactory;

    public async Task AddAsync(OperationalIssue issue, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.OperationalIssues.Add(new OperationalIssueEntity
        {
            Id = issue.Id,
            Category = issue.Category,
            Severity = issue.Severity,
            Source = issue.Source,
            Message = issue.Message,
            Details = issue.Details,
            OccurredAt = issue.OccurredAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperationalIssue>> GetRecentAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Abs(hours));
        IQueryable<OperationalIssueEntity> query = dbContext.OperationalIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category == category);
        }

        List<OperationalIssueEntity> entities = await query.ToListAsync(cancellationToken);
        return entities
            .Where(x => x.OccurredAt >= cutoff)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new OperationalIssue(x.Id, x.Category, x.Severity, x.Source, x.Message, x.Details, x.OccurredAt))
            .ToArray();
    }
}
