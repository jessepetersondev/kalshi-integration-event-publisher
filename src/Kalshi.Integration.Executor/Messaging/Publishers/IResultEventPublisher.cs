using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;

namespace Kalshi.Integration.Executor.Messaging.Publishers;

public interface IResultEventPublisher
{
    Task PublishAsync(ApplicationEventEnvelope sourceEvent, ExecutionResult result, CancellationToken cancellationToken);
}
