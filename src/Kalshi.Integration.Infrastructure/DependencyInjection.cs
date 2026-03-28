using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var normalizedProvider = DatabaseProviders.Normalize(configuration.GetValue<string>($"{DatabaseOptions.SectionName}:Provider"));
        var applyMigrationsOnStartup = configuration.GetValue($"{DatabaseOptions.SectionName}:ApplyMigrationsOnStartup", true);
        var connectionString = configuration.GetConnectionString("KalshiIntegration")
            ?? (normalizedProvider == DatabaseProviders.Sqlite ? "Data Source=kalshi-integration-sandbox.db" : null);

        DatabaseProviders.EnsureConnectionString(connectionString);

        services.Configure<DatabaseOptions>(options =>
        {
            options.Provider = normalizedProvider;
            options.ApplyMigrationsOnStartup = applyMigrationsOnStartup;
        });
        services.Configure<EventPublisherOptions>(configuration.GetSection(EventPublisherOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddDbContext<KalshiIntegrationDbContext>(options => ConfigureDatabaseProvider(options, normalizedProvider, connectionString!));
        services.AddScoped<ITradingRepository, EfTradingRepository>();
        services.AddSingleton<IOperationalIssueStore, InMemoryOperationalIssueStore>();
        services.AddSingleton<IAuditRecordStore, InMemoryAuditRecordStore>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<InMemoryApplicationEventPublisher>();
        services.AddSingleton<IConnectionFactory>(sp => CreateRabbitMqConnectionFactory(sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<RabbitMqApplicationEventPublisher>();
        services.AddSingleton<IApplicationEventPublisher>(sp => ResolveApplicationEventPublisher(sp));

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    private static IApplicationEventPublisher ResolveApplicationEventPublisher(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<EventPublisherOptions>>().Value;
        return string.Equals(options.Provider, EventPublisherProviders.RabbitMq, StringComparison.OrdinalIgnoreCase)
            ? serviceProvider.GetRequiredService<RabbitMqApplicationEventPublisher>()
            : serviceProvider.GetRequiredService<InMemoryApplicationEventPublisher>();
    }

    private static void ConfigureDatabaseProvider(DbContextOptionsBuilder options, string provider, string connectionString)
    {
        switch (provider)
        {
            case DatabaseProviders.Sqlite:
                options.UseSqlite(connectionString);
                break;
            case DatabaseProviders.SqlServer:
                options.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure();
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }
    }

    private static ConnectionFactory CreateRabbitMqConnectionFactory(RabbitMqOptions options)
    {
        return new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.UserName,
            Password = options.Password,
            ClientProvidedName = options.ClientProvidedName,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
        };
    }
}
