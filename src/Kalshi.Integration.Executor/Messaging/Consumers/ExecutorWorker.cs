using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Executor.Messaging.Consumers;

public sealed class ExecutorWorker : BackgroundService
{
    private readonly IExecutorMessagePump _messagePump;
    private readonly ILogger<ExecutorWorker> _logger;

    public ExecutorWorker(IExecutorMessagePump messagePump, ILogger<ExecutorWorker> logger)
    {
        _messagePump = messagePump;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executor worker shell starting.");

        try
        {
            await _messagePump.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Executor worker shell stopped.");
    }
}
