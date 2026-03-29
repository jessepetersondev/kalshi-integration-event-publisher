using System.Collections.Concurrent;

namespace Kalshi.Integration.Executor.Persistence.Repositories;

public sealed class InMemoryConsumedEventStore : IConsumedEventStore
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _processedEventIds = new();

    public Task<bool> HasProcessedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_processedEventIds.ContainsKey(eventId));
    }

    public Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _processedEventIds[eventId] = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
}
