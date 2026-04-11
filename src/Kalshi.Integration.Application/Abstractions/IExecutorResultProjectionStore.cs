using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Applies executor result events transactionally with publisher-owned projections.
/// </summary>
public interface IExecutorResultProjectionStore
{
    Task<bool> ApplyExecutorResultAsync(
        ApplicationEventEnvelope resultEvent,
        ResultProjectionMutation mutation,
        CancellationToken cancellationToken = default);
}
