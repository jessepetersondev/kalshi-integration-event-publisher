using Kalshi.Integration.Executor.Configuration;
using Kalshi.Integration.Executor.Execution.Services;
using Kalshi.Integration.Executor.KalshiApi.Clients;
using Kalshi.Integration.Executor.Messaging.Consumers;
using Kalshi.Integration.Executor.Messaging.Publishers;
using Kalshi.Integration.Executor.Observability;
using Kalshi.Integration.Executor.Persistence.Repositories;
using Kalshi.Integration.Executor.Routing;
using Kalshi.Integration.Executor.Routing.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalshi.Integration.Executor;

public static class DependencyInjection
{
    public static IServiceCollection AddExecutor(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<ExecutorOptions>()
            .Bind(configuration.GetSection(ExecutorOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.RoutingBindings.Any(binding => !string.IsNullOrWhiteSpace(binding)),
                $"{ExecutorOptions.SectionName}:RoutingBindings must contain at least one binding.")
            .ValidateOnStart();

        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OpenTelemetryOptions>()
            .Bind(configuration.GetSection(OpenTelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => string.IsNullOrWhiteSpace(options.OtlpEndpoint) || Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _),
                $"{OpenTelemetryOptions.SectionName}:OtlpEndpoint must be an absolute URI when configured.")
            .ValidateOnStart();

        services.AddExecutorObservability(configuration, environment);
        services.AddSingleton<IConsumedEventStore, InMemoryConsumedEventStore>();
        services.AddSingleton<IKalshiExecutionClient, NoOpKalshiExecutionClient>();
        services.AddSingleton<IResultEventPublisher, LoggingResultEventPublisher>();
        services.AddSingleton<IEventProcessor, SupportedEventProcessor>();
        services.AddSingleton<IEventRouter, EventRouter>();
        services.AddSingleton<IExecutorShellService, ExecutorShellService>();
        services.AddSingleton<IExecutorMessagePump, RabbitMqConsumerShell>();
        services.AddHostedService<ExecutorWorker>();

        return services;
    }
}
