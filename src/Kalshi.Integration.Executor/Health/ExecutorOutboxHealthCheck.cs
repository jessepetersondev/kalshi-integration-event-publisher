using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Health;

/// <summary>
/// Reports executor outbox health based on pending age and retry exhaustion state.
/// </summary>
public sealed class ExecutorOutboxHealthCheck(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options) : IHealthCheck
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly RabbitMqOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        ExecutorOutboxHealthService healthService = scope.ServiceProvider.GetRequiredService<ExecutorOutboxHealthService>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Contracts.Reliability.OutboxHealthSnapshot snapshot = await healthService.GetSnapshotAsync(now, cancellationToken);
        TimeSpan oldestPendingAge = snapshot.OldestPendingCreatedAt.HasValue
            ? now - snapshot.OldestPendingCreatedAt.Value
            : TimeSpan.Zero;

        Dictionary<string, object> data = new()
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
            return HealthCheckResult.Unhealthy("Executor outbox requires intervention or is excessively delayed.", data: data);
        }

        if (snapshot.PendingCount > 0 && oldestPendingAge.TotalSeconds >= _options.OutboxDegradedAgeSeconds)
        {
            return HealthCheckResult.Degraded("Executor outbox is delayed but still retrying automatically.", data: data);
        }

        return HealthCheckResult.Healthy("Executor outbox is healthy.", data);
    }
}
