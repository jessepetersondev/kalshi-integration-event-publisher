using System.Text.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Handlers;
using Kalshi.Integration.Executor.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Kalshi.Integration.Infrastructure.Messaging;

namespace Kalshi.Integration.Executor.Messaging;

/// <summary>
/// Replays stale non-terminal executions from durable state so executor gaps close after crashes,
/// broker interruptions, or ack/publish loss.
/// </summary>
public sealed class ExecutionRepairBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<ExecutionRepairBackgroundService> _logger;

    public ExecutionRepairBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<ExecutionRepairBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ExecutorDbContext>();
                var handler = scope.ServiceProvider.GetRequiredService<OrderCreatedHandler>();
                var issueRecorder = scope.ServiceProvider.GetRequiredService<ExecutorOperationalIssueRecorder>();
                var replayCutoff = DateTimeOffset.UtcNow.AddSeconds(-_options.RepairGraceSeconds);

                var staleExecutions = (await dbContext.ExecutionRecords
                    .AsNoTracking()
                    .Where(x => x.TerminalResultQueuedAt == null)
                    .ToListAsync(stoppingToken))
                    .Where(x => !x.LeaseExpiresAt.HasValue || x.LeaseExpiresAt < replayCutoff)
                    .Where(x => x.UpdatedAt <= replayCutoff)
                    .Take(_options.RepairBatchSize)
                    .Select(x => new { x.CommandEventId, x.PublisherOrderId })
                    .ToList();

                foreach (var staleExecution in staleExecutions)
                {
                    var inbound = await dbContext.InboundMessages
                        .AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Id == staleExecution.CommandEventId, stoppingToken);

                    if (inbound is null)
                    {
                        await issueRecorder.AddAsync(
                            "reliability",
                            "warning",
                            "execution-repair",
                            $"Execution repair could not find the source command envelope for publisher order '{staleExecution.PublisherOrderId}'.",
                            $"commandEventId={staleExecution.CommandEventId}",
                            stoppingToken);
                        continue;
                    }

                    try
                    {
                        var envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(inbound.PayloadJson, SerializerOptions)
                            ?? throw new InvalidOperationException($"Inbound payload '{inbound.Id}' could not be deserialized.");

                        await handler.HandleAsync(envelope, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        await issueRecorder.AddAsync(
                            "reliability",
                            "warning",
                            "execution-repair",
                            $"Execution repair replay failed for publisher order '{staleExecution.PublisherOrderId}'.",
                            exception.Message,
                            stoppingToken);
                        _logger.LogWarning(exception, "Execution repair replay failed for publisher order {PublisherOrderId}.", staleExecution.PublisherOrderId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Executor repair iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.OutboxPollingIntervalMilliseconds), stoppingToken);
        }
    }
}
