using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;

namespace Kalshi.Integration.Executor.Routing.Handlers;

public interface IEventProcessor
{
    bool CanHandle(ApplicationEventEnvelope envelope);

    Task<ExecutionResult> HandleAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken);
}
