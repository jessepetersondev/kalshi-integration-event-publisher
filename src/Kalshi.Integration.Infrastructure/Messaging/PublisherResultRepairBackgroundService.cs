using System.Text.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Replays persisted-but-unapplied executor result events so projection gaps close automatically
/// after process interruption or partial failure.
/// </summary>
public sealed class PublisherResultRepairBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<PublisherResultRepairBackgroundService> _logger;

    public PublisherResultRepairBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<PublisherResultRepairBackgroundService> logger)
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
                var dbContext = scope.ServiceProvider.GetRequiredService<KalshiIntegrationDbContext>();
                var tradingService = scope.ServiceProvider.GetRequiredService<TradingService>();

                var pending = (await dbContext.ResultEvents
                    .Where(x => x.AppliedAt == null)
                    .ToListAsync(stoppingToken))
                    .OrderBy(x => x.OccurredAt)
                    .Take(_options.Value.RepairBatchSize)
                    .ToList();

                foreach (var resultEvent in pending)
                {
                    try
                    {
                        var envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(resultEvent.PayloadJson, SerializerOptions);
                        if (envelope is null)
                        {
                            continue;
                        }

                        await tradingService.ApplyExecutorResultAsync(envelope, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        resultEvent.ApplyAttemptCount++;
                        resultEvent.LastApplyAttemptAt = DateTimeOffset.UtcNow;
                        resultEvent.LastError = exception.Message;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogError(exception, "Failed to repair unapplied result event {ResultEventId}.", resultEvent.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Publisher result repair iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.OutboxPollingIntervalMilliseconds), stoppingToken);
        }
    }
}
