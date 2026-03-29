using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Executor.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kalshi.Integration.Executor.Observability;

public static class DependencyInjection
{
    public static IServiceCollection AddExecutorObservability(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(options.ServiceName, serviceVersion: options.ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName,
                }))
            .WithTracing(tracing =>
            {
                tracing.AddSource(KalshiTelemetry.ActivitySourceName);

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = new Uri(options.OtlpEndpoint, UriKind.Absolute);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(KalshiTelemetry.MeterName)
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = new Uri(options.OtlpEndpoint, UriKind.Absolute);
                    });
                }
            });

        return services;
    }
}
