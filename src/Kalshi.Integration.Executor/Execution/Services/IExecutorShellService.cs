using Kalshi.Integration.Application.Events;

namespace Kalshi.Integration.Executor.Execution.Services;

public interface IExecutorShellService
{
    Task HandleAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken);
}
