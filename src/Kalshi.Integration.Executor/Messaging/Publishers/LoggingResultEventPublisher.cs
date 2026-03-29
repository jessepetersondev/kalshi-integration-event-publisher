using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Configuration;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Messaging.Publishers;

public sealed class LoggingResultEventPublisher : IResultEventPublisher
{
    private readonly ILogger<LoggingResultEventPublisher> _logger;
    private readonly ExecutorOptions _options;

    public LoggingResultEventPublisher(IOptions<ExecutorOptions> options, ILogger<LoggingResultEventPublisher> logger)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task PublishAsync(ApplicationEventEnvelope sourceEvent, ExecutionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ExecutorTelemetry.ResultEventsPublished.Add(1);
        _logger.LogInformation(
            "Executor shell would publish result event {ResultEventName} for {SourceEventName} using {ServiceName} {ServiceVersion}.",
            result.ResultEventName,
            sourceEvent.Name,
            _options.ServiceName,
            _options.ServiceVersion);

        return Task.CompletedTask;
    }
}
