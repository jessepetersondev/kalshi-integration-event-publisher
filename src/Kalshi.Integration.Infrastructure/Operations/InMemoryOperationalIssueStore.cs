using System.Collections.Concurrent;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Provides storage operations for in memory operational issue.
/// </summary>


public sealed class InMemoryOperationalIssueStore : IOperationalIssueStore
{
    private readonly ConcurrentQueue<OperationalIssue> _issues = new();

    public Task AddAsync(OperationalIssue issue, CancellationToken cancellationToken = default)
    {
        _issues.Enqueue(issue);

        while (_issues.Count > 500 && _issues.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperationalIssue>> GetRecentAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Abs(hours));
        var issues = _issues
            .Where(issue => issue.OccurredAt >= cutoff)
            .Where(issue => string.IsNullOrWhiteSpace(category) || string.Equals(issue.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Task.FromResult<IReadOnlyList<OperationalIssue>>(issues);
    }
}
