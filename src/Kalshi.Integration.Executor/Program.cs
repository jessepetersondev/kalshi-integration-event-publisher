using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Executor;
using Kalshi.Integration.Executor.Configuration;
using Kalshi.Integration.Executor.Infrastructure;
using Kalshi.Integration.Executor.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExecutor(builder.Configuration);

OpenTelemetryOptions configuredOpenTelemetryOptions = builder.Configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

builder.Services.AddOptions<OpenTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(OpenTelemetryOptions.SectionName))
    .Validate(options => string.IsNullOrWhiteSpace(options.OtlpEndpoint) || Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _), $"{OpenTelemetryOptions.SectionName}:OtlpEndpoint must be an absolute URI when configured.")
    .ValidateOnStart();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            configuredOpenTelemetryOptions.ServiceName,
            serviceVersion: string.IsNullOrWhiteSpace(configuredOpenTelemetryOptions.ServiceVersion) ? "v1" : configuredOpenTelemetryOptions.ServiceVersion)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
        }))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(KalshiTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true);

        if (!string.IsNullOrWhiteSpace(configuredOpenTelemetryOptions.OtlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(configuredOpenTelemetryOptions.OtlpEndpoint, UriKind.Absolute);
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(KalshiTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();

        if (!string.IsNullOrWhiteSpace(configuredOpenTelemetryOptions.OtlpEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(configuredOpenTelemetryOptions.OtlpEndpoint, UriKind.Absolute);
            });
        }
    });

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    ExecutorDbContext dbContext = scope.ServiceProvider.GetRequiredService<ExecutorDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.MapGet("/", () => Results.Ok(new { service = "Kalshi.Integration.Executor" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live", StringComparer.OrdinalIgnoreCase),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync,
});

await app.RunAsync();
