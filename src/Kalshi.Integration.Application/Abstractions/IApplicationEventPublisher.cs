using Kalshi.Integration.Application.Events;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Publishes i application event.
/// </summary>


public interface IApplicationEventPublisher
{
    Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default);
}
