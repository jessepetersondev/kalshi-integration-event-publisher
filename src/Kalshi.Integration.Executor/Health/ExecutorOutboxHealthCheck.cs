using Kalshi.Integration.Executor.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Kalshi.Integration.Infrastructure.Messaging;

namespace Kalshi.Integration.Executor.Health;

/// <summary>
/// Reports executor outbox health based on pending age and retry exhaustion state.
/// </summary>
public sealed class ExecutorOutboxHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqOptions _options;

    public ExecutorOutboxHealthCheck(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<ExecutorOutboxHealthService>();
        var now = DateTimeOffset.UtcNow;
        var snapshot = await healthService.GetSnapshotAsync(now, cancellationToken);
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
            return HealthCheckResult.Unhealthy("Executor outbox requires intervention or is excessively delayed.", data: data);
        }

        if (snapshot.PendingCount > 0 && oldestPendingAge.TotalSeconds >= _options.OutboxDegradedAgeSeconds)
        {
            return HealthCheckResult.Degraded("Executor outbox is delayed but still retrying automatically.", data: data);
        }

        return HealthCheckResult.Healthy("Executor outbox is healthy.", data);
    }
}
