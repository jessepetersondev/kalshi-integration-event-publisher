using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Routing.Handlers;

namespace Kalshi.Integration.Executor.Routing;

public sealed class EventRouter : IEventRouter
{
    private readonly IReadOnlyCollection<IEventProcessor> _handlers;

    public EventRouter(IEnumerable<IEventProcessor> handlers)
    {
        _handlers = handlers.ToArray();
    }

    public Task<ExecutionResult> RouteAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(envelope));
        return handler is null
            ? Task.FromException<ExecutionResult>(new NotSupportedException($"No executor handler is registered for event '{envelope.Name}'."))
            : handler.HandleAsync(envelope, cancellationToken);
    }
}
