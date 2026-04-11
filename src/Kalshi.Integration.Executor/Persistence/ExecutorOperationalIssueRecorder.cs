using Kalshi.Integration.Executor.Persistence.Entities;

namespace Kalshi.Integration.Executor.Persistence;

/// <summary>
/// Persists executor-local operational issues for monitoring and repair workflows.
/// </summary>
public sealed class ExecutorOperationalIssueRecorder(ExecutorDbContext dbContext)
{
    private readonly ExecutorDbContext _dbContext = dbContext;

    public async Task AddAsync(
        string category,
        string severity,
        string source,
        string message,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        _dbContext.OperationalIssues.Add(new ExecutorOperationalIssueEntity
        {
            Id = Guid.NewGuid(),
            Category = category,
            Severity = severity,
            Source = source,
            Message = message,
            Details = details,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
