using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;

namespace Kalshi.Integration.Executor.Routing;

public interface IEventRouter
{
    Task<ExecutionResult> RouteAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken);
}
