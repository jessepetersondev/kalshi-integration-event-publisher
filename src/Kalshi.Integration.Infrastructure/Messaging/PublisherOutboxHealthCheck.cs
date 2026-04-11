using Kalshi.Integration.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Reports publisher command outbox health based on pending age and manual-intervention state.
/// </summary>
public sealed class PublisherOutboxHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqOptions _options;

    public PublisherOutboxHealthCheck(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IPublisherCommandOutboxStore>();
        var now = DateTimeOffset.UtcNow;
        var snapshot = await outboxStore.GetHealthSnapshotAsync(now, cancellationToken);
        var oldestPendingAge = snapshot.OldestPendingCreatedAt.HasValue
            ? now - snapshot.OldestPendingCreatedAt.Value
            : TimeSpan.Zero;

        var data = new Dictionary<string, object>
        {
            ["pendingCount"] = snapshot.PendingCount,
            ["manualInterventionCount"] = snapshot.ManualInterventionCount,
        };

        if (snapshot.OldestPendingCreatedAt.HasValue)
        {
            data["oldestPendingAgeSeconds"] = oldestPendingAge.TotalSeconds;
        }

        if (snapshot.ManualInterventionCount > 0 || oldestPendingAge.TotalSeconds >= _options.OutboxUnhealthyAgeSeconds)
        {
            return HealthCheckResult.Unhealthy("Publisher outbox requires intervention or is excessively delayed.", data: data);
        }

        if (snapshot.PendingCount > 0 && oldestPendingAge.TotalSeconds >= _options.OutboxDegradedAgeSeconds)
        {
            return HealthCheckResult.Degraded("Publisher outbox is delayed but still retrying automatically.", data: data);
        }

        return HealthCheckResult.Healthy("Publisher outbox is healthy.", data);
    }
}
