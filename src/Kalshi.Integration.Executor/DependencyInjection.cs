using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Handlers;
using Kalshi.Integration.Executor.Health;
using Kalshi.Integration.Executor.Messaging;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Executor;

public static class DependencyInjection
{
    public static IServiceCollection AddExecutor(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Executor") ?? "Data Source=kalshi-integration-executor.db";
        KalshiApiOptions kalshiApiOptions = configuration.GetSection(KalshiApiOptions.SectionName).Get<KalshiApiOptions>() ?? new KalshiApiOptions();

        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options =>
            {
                options.Mandatory = true;
            })
            .ValidateOnStart();

        services.AddOptions<KalshiApiOptions>()
            .Bind(configuration.GetSection(KalshiApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContextFactory<ExecutorDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ExecutorDbContext>(serviceProvider => serviceProvider.GetRequiredService<IDbContextFactory<ExecutorDbContext>>().CreateDbContext());

        services.AddHttpClient<IKalshiApiClient, KalshiApiClient>((serviceProvider, client) =>
            {
                KalshiApiOptions options = serviceProvider.GetRequiredService<IOptions<KalshiApiOptions>>().Value;
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

        services.AddSingleton<IConnectionFactory>(sp =>
        {
            RabbitMqOptions options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
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
        });

        services.AddSingleton<RabbitMqTopologyBootstrapper>();
        services.AddSingleton<RabbitMqQueueInspector>();
        services.AddSingleton<RabbitMqApplicationEventPublisher>();
        services.AddSingleton<IApplicationEventPublisher>(sp => sp.GetRequiredService<RabbitMqApplicationEventPublisher>());
        services.AddScoped<ExecutionReliabilityPolicy>();
        services.AddScoped<RabbitMqResultEventPublisher>();
        services.AddScoped<RabbitMqInboundEventPublisher>();
        services.AddScoped<DeadLetterEventPublisher>();
        services.AddScoped<OrderCreatedHandler>();
        services.AddScoped<ExecutorOutboxDispatcher>();
        services.AddScoped<ExecutorOperationalIssueRecorder>();
        services.AddScoped<ExecutorOutboxHealthService>();
        services.AddHostedService<ExecutorInboundConsumer>();
        services.AddHostedService<ExecutorOutboxBackgroundService>();
        services.AddHostedService<ExecutionRepairBackgroundService>();
        services.AddHostedService<ExecutorReliabilityMonitorBackgroundService>();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
            .AddCheck<ExecutorDatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<ExecutorOutboxHealthCheck>("executor-outbox", tags: ["ready"])
            .AddCheck<RabbitMqQueuesHealthCheck>("rabbitmq-queues", tags: ["ready"]);

        return services;
    }

    private static TimeSpan GetCircuitBreakerSamplingDuration(int timeoutSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(timeoutSeconds * 2, 30));
    }
}
