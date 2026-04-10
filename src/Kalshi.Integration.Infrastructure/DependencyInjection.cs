using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Kalshi.Integration.Infrastructure.Integrations.NodeGateway;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure;

/// <summary>
/// Registers infrastructure services, integrations, and persistence components with the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services, integrations, persistence, and health checks for the publisher.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var normalizedProvider = DatabaseProviders.Normalize(configuration.GetValue<string>($"{DatabaseOptions.SectionName}:Provider"));
        var applyMigrationsOnStartup = configuration.GetValue($"{DatabaseOptions.SectionName}:ApplyMigrationsOnStartup", true);
        var connectionString = configuration.GetConnectionString("KalshiIntegration")
            ?? (normalizedProvider == DatabaseProviders.Sqlite ? "Data Source=kalshi-integration-event-publisher.db" : null);
        var normalizedEventPublisherProvider = EventPublisherProviders.Normalize(configuration.GetValue<string>($"{EventPublisherOptions.SectionName}:Provider"));
        var enableRabbitMqResultConsumer = configuration.GetValue($"{RabbitMqOptions.SectionName}:EnableResultConsumer", true);
        var kalshiApiOptions = configuration.GetSection(KalshiApiOptions.SectionName).Get<KalshiApiOptions>() ?? new KalshiApiOptions();
        var nodeGatewayOptions = configuration.GetSection(NodeGatewayOptions.SectionName).Get<NodeGatewayOptions>() ?? new NodeGatewayOptions();

        DatabaseProviders.EnsureConnectionString(connectionString);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => TryNormalizeDatabaseProvider(options.Provider), $"{DatabaseOptions.SectionName}:Provider must be one of: {DatabaseProviders.Sqlite}, {DatabaseProviders.SqlServer}, AzureSql.")
            .PostConfigure(options =>
            {
                options.Provider = normalizedProvider;
                options.ApplyMigrationsOnStartup = applyMigrationsOnStartup;
            })
            .ValidateOnStart();

        services.AddOptions<EventPublisherOptions>()
            .Bind(configuration.GetSection(EventPublisherOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => TryNormalizeEventPublisherProvider(options.Provider), $"{EventPublisherOptions.SectionName}:Provider must be one of: {EventPublisherProviders.InMemory}, {EventPublisherProviders.RabbitMq}.")
            .PostConfigure(options => options.Provider = normalizedEventPublisherProvider)
            .ValidateOnStart();

        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KalshiApiOptions>()
            .Bind(configuration.GetSection(KalshiApiOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), $"{KalshiApiOptions.SectionName}:BaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddOptions<NodeGatewayOptions>()
            .Bind(configuration.GetSection(NodeGatewayOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), $"{NodeGatewayOptions.SectionName}:BaseUrl must be an absolute URL.")
            .Validate(options => options.HealthPath.StartsWith('/'), $"{NodeGatewayOptions.SectionName}:HealthPath must start with '/'.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationPropagationHandler>();
        services.AddHttpClient<IKalshiApiClient, KalshiApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<KalshiApiOptions>>().Value;
                client.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddStandardResilienceHandler(resilienceOptions =>
            {
                resilienceOptions.Retry.MaxRetryAttempts = 2;
                resilienceOptions.AttemptTimeout.Timeout = TimeSpan.FromSeconds(kalshiApiOptions.TimeoutSeconds);
                resilienceOptions.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(kalshiApiOptions.TimeoutSeconds * 3, kalshiApiOptions.TimeoutSeconds));
                resilienceOptions.CircuitBreaker.SamplingDuration = GetCircuitBreakerSamplingDuration(kalshiApiOptions.TimeoutSeconds);
            });
        services.AddHttpClient<INodeGatewayClient, NodeGatewayClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<NodeGatewayOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddStandardResilienceHandler(resilienceOptions =>
            {
                resilienceOptions.Retry.MaxRetryAttempts = nodeGatewayOptions.RetryAttempts;
                resilienceOptions.AttemptTimeout.Timeout = TimeSpan.FromSeconds(nodeGatewayOptions.TimeoutSeconds);
                resilienceOptions.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(nodeGatewayOptions.TimeoutSeconds * Math.Max(1, nodeGatewayOptions.RetryAttempts + 1), nodeGatewayOptions.TimeoutSeconds));
                resilienceOptions.CircuitBreaker.SamplingDuration = GetCircuitBreakerSamplingDuration(nodeGatewayOptions.TimeoutSeconds);
            });

        services.AddDbContextFactory<KalshiIntegrationDbContext>(options => ConfigureDatabaseProvider(options, normalizedProvider, connectionString!));
        services.AddScoped<KalshiIntegrationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<KalshiIntegrationDbContext>>().CreateDbContext());
        services.AddScoped<EfTradingRepository>();
        services.AddScoped<ITradeIntentRepository>(serviceProvider => serviceProvider.GetRequiredService<EfTradingRepository>());
        services.AddScoped<IOrderRepository>(serviceProvider => serviceProvider.GetRequiredService<EfTradingRepository>());
        services.AddScoped<IPositionSnapshotRepository>(serviceProvider => serviceProvider.GetRequiredService<EfTradingRepository>());
        services.AddScoped<IOperationalIssueStore, EfOperationalIssueStore>();
        services.AddScoped<IAuditRecordStore, EfAuditRecordStore>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<KalshiBridgeService>();
        services.AddSingleton<InMemoryApplicationEventPublisher>();
        services.AddSingleton<IConnectionFactory>(sp => CreateRabbitMqConnectionFactory(sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<RabbitMqApplicationEventPublisher>();
        services.AddSingleton<IApplicationEventPublisher>(ResolveApplicationEventPublisher);
        if (enableRabbitMqResultConsumer)
        {
            services.AddHostedService<RabbitMqResultEventConsumer>();
        }

        var healthChecks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

        if (nodeGatewayOptions.Enabled && nodeGatewayOptions.IncludeInReadiness)
        {
            healthChecks.AddCheck<NodeGatewayReadinessHealthCheck>("node-gateway", tags: ["ready"]);
        }

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

    private static bool TryNormalizeDatabaseProvider(string? provider)
    {
        try
        {
            _ = DatabaseProviders.Normalize(provider);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryNormalizeEventPublisherProvider(string? provider)
    {
        try
        {
            _ = EventPublisherProviders.Normalize(provider);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TimeSpan GetCircuitBreakerSamplingDuration(int timeoutSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(timeoutSeconds * 2, 30));
    }
}
