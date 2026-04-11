using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Messaging;

public sealed class ExecutorOutboxBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<ExecutorOutboxBackgroundService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IOptions<RabbitMqOptions> _options = options;
    private readonly ILogger<ExecutorOutboxBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                ExecutorOutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ExecutorOutboxDispatcher>();
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
