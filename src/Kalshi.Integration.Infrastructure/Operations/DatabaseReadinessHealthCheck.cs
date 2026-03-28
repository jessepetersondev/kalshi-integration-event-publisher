using System.Diagnostics;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Infrastructure.Operations;

public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private static readonly Action<ILogger, string, string, bool, double, Exception?> DependencyCheckCompleted =
        LoggerMessage.Define<string, string, bool, double>(
            LogLevel.Information,
            new EventId(1000, nameof(DependencyCheckCompleted)),
            "Dependency check {Dependency} {Operation} completed with canConnect={CanConnect} in {ElapsedMs} ms.");

    private static readonly Action<ILogger, string, string, double, Exception?> DependencyCheckFailed =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Error,
            new EventId(1001, nameof(DependencyCheckFailed)),
            "Dependency check {Dependency} {Operation} failed after {ElapsedMs} ms.");

    private readonly KalshiIntegrationDbContext _dbContext;
    private readonly ILogger<DatabaseReadinessHealthCheck> _logger;

    public DatabaseReadinessHealthCheck(KalshiIntegrationDbContext dbContext, ILogger<DatabaseReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();

            DependencyCheckCompleted(
                _logger,
                "sqlite",
                "database.readiness",
                canConnect,
                stopwatch.Elapsed.TotalMilliseconds,
                null);

            return canConnect
                ? HealthCheckResult.Healthy("SQLite connectivity verified.")
                : HealthCheckResult.Unhealthy("SQLite connectivity check failed.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            DependencyCheckFailed(
                _logger,
                "sqlite",
                "database.readiness",
                stopwatch.Elapsed.TotalMilliseconds,
                exception);

            return HealthCheckResult.Unhealthy("SQLite readiness check threw an exception.", exception);
        }
    }
}
