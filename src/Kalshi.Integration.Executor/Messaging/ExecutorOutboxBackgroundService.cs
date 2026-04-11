using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Kalshi.Integration.Infrastructure.Messaging;

namespace Kalshi.Integration.Executor.Messaging;

public sealed class ExecutorOutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<ExecutorOutboxBackgroundService> _logger;

    public ExecutorOutboxBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<ExecutorOutboxBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ExecutorOutboxDispatcher>();
                await dispatcher.DrainDueMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Executor outbox dispatcher iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.OutboxPollingIntervalMilliseconds), stoppingToken);
        }
    }
}
