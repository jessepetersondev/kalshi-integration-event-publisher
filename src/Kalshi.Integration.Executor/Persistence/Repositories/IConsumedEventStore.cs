namespace Kalshi.Integration.Executor.Persistence.Repositories;

public interface IConsumedEventStore
{
    Task<bool> HasProcessedAsync(Guid eventId, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken);
}
