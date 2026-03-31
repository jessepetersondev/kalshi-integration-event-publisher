using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Creates kalshi integration db context instances.
/// </summary>


public sealed class KalshiIntegrationDbContextFactory : IDesignTimeDbContextFactory<KalshiIntegrationDbContext>
{
    public KalshiIntegrationDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration(args);
        var provider = DatabaseProviders.Normalize(configuration.GetValue<string>($"{DatabaseOptions.SectionName}:Provider"));
        var connectionString = configuration.GetConnectionString("KalshiIntegration")
            ?? (provider == DatabaseProviders.Sqlite ? "Data Source=kalshi-integration-event-publisher.db" : null);

        DatabaseProviders.EnsureConnectionString(connectionString);

        var optionsBuilder = new DbContextOptionsBuilder<KalshiIntegrationDbContext>();
        switch (provider)
        {
            case DatabaseProviders.Sqlite:
                optionsBuilder.UseSqlite(connectionString);
                break;
            case DatabaseProviders.SqlServer:
                optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure();
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }

        return new KalshiIntegrationDbContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration(string[] args)
    {
        var apiProjectPath = ResolveApiProjectPath();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        return new ConfigurationBuilder()
            .SetBasePath(apiProjectPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    private static string ResolveApiProjectPath()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Kalshi.Integration.Api"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Kalshi.Integration.Api"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Kalshi.Integration.Api"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, "appsettings.json")))
            {
                return fullPath;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
