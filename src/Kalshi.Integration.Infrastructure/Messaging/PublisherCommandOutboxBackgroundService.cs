using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Continuously drains the publisher command outbox so transient broker failures
/// resolve through eventual retry instead of manual intervention.
/// </summary>
public sealed class PublisherCommandOutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<PublisherCommandOutboxBackgroundService> _logger;

    public PublisherCommandOutboxBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<PublisherCommandOutboxBackgroundService> logger)
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
                var dispatcher = scope.ServiceProvider.GetRequiredService<PublisherCommandOutboxDispatcher>();
                await dispatcher.DrainDueMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Publisher command outbox dispatcher iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.OutboxPollingIntervalMilliseconds), stoppingToken);
        }
    }
}
