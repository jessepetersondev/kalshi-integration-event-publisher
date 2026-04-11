using Kalshi.Integration.Executor.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalshi.Integration.Executor.Health;

public sealed class ExecutorDatabaseHealthCheck : IHealthCheck
{
    private readonly ExecutorDbContext _dbContext;

    public ExecutorDatabaseHealthCheck(ExecutorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Executor database is reachable.")
            : HealthCheckResult.Unhealthy("Executor database is not reachable.");
    }
}
