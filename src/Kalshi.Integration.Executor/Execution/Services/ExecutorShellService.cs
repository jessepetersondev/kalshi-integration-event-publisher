using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Messaging.Publishers;
using Kalshi.Integration.Executor.Observability;
using Kalshi.Integration.Executor.Persistence.Repositories;
using Kalshi.Integration.Executor.Routing;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Executor.Execution.Services;

public sealed class ExecutorShellService : IExecutorShellService
{
    private readonly IConsumedEventStore _consumedEventStore;
    private readonly IEventRouter _eventRouter;
    private readonly ILogger<ExecutorShellService> _logger;
    private readonly IResultEventPublisher _resultEventPublisher;

    public ExecutorShellService(
        IConsumedEventStore consumedEventStore,
        IEventRouter eventRouter,
        IResultEventPublisher resultEventPublisher,
        ILogger<ExecutorShellService> logger)
    {
        _consumedEventStore = consumedEventStore;
        _eventRouter = eventRouter;
        _resultEventPublisher = resultEventPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (await _consumedEventStore.HasProcessedAsync(envelope.Id, cancellationToken))
        {
            ExecutorTelemetry.MessagesSkipped.Add(1);
            _logger.LogInformation("Skipping duplicate executor event {EventId}.", envelope.Id);
            return;
        }

        var result = await _eventRouter.RouteAsync(envelope, cancellationToken);
        await _consumedEventStore.MarkProcessedAsync(envelope.Id, cancellationToken);
        await _resultEventPublisher.PublishAsync(envelope, result, cancellationToken);
        ExecutorTelemetry.MessagesHandled.Add(1);
    }
}
