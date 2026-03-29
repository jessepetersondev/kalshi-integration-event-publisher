using Kalshi.Integration.Application.Events;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Executor.KalshiApi.Clients;

public sealed class NoOpKalshiExecutionClient : IKalshiExecutionClient
{
    private readonly ILogger<NoOpKalshiExecutionClient> _logger;

    public NoOpKalshiExecutionClient(ILogger<NoOpKalshiExecutionClient> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Executor shell received {EventName} for resource {ResourceId}. Kalshi API execution is not implemented in JPC-1563.",
            envelope.Name,
            envelope.ResourceId);

        return Task.CompletedTask;
    }
}
