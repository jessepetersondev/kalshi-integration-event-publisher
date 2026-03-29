using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.KalshiApi.Clients;
using Kalshi.Integration.Executor.Routing;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Executor.Routing.Handlers;

public sealed class SupportedEventProcessor : IEventProcessor
{
    private readonly IKalshiExecutionClient _kalshiExecutionClient;
    private readonly ILogger<SupportedEventProcessor> _logger;

    public SupportedEventProcessor(
        IKalshiExecutionClient kalshiExecutionClient,
        ILogger<SupportedEventProcessor> logger)
    {
        _kalshiExecutionClient = kalshiExecutionClient;
        _logger = logger;
    }

    public bool CanHandle(ApplicationEventEnvelope envelope)
        => ExecutorEventNames.TryGetResultEventName(envelope.Name, out _);

    public async Task<ExecutionResult> HandleAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!ExecutorEventNames.TryGetResultEventName(envelope.Name, out var resultEventName))
        {
            throw new NotSupportedException($"The executor shell does not support event '{envelope.Name}'.");
        }

        await _kalshiExecutionClient.ExecuteAsync(envelope, cancellationToken);
        _logger.LogInformation("Executor shell routed event {EventName} to the placeholder Kalshi client.", envelope.Name);
        return ExecutionResult.Success(resultEventName);
    }
}
