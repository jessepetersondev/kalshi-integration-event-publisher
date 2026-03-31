using System.Text;
using Asp.Versioning;
using Kalshi.Integration.Api.Configuration;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application;
using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Infrastructure;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Compose the API host in one place so local demo, test, and cloud deployment
// paths all share the same auth, observability, persistence, and dashboard setup.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration);

var configuredJwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    configuredJwtOptions.SigningKey ??= JwtOptions.DevelopmentSigningKey;
}

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .PostConfigure(options =>
    {
        options.Issuer = string.IsNullOrWhiteSpace(options.Issuer) ? JwtOptions.DefaultIssuer : options.Issuer.Trim();
        options.Audience = string.IsNullOrWhiteSpace(options.Audience) ? JwtOptions.DefaultAudience : options.Audience.Trim();

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            options.SigningKey ??= JwtOptions.DevelopmentSigningKey;
        }
    })
    .ValidateDataAnnotations()
    .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey), "Authentication:Jwt:SigningKey must be configured.")
    .Validate(options => (options.SigningKey?.Length ?? 0) >= 32, "Authentication:Jwt:SigningKey must be at least 32 characters long.")
    .ValidateOnStart();

var configuredOpenApiOptions = builder.Configuration.GetSection(OpenApiOptions.SectionName).Get<OpenApiOptions>() ?? new OpenApiOptions();
var configuredOpenTelemetryOptions = builder.Configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

builder.Services.AddOptions<OpenApiOptions>()
    .Bind(builder.Configuration.GetSection(OpenApiOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<OpenTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(OpenTelemetryOptions.SectionName))
    .Validate(options => string.IsNullOrWhiteSpace(options.OtlpEndpoint) || Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _), $"{OpenTelemetryOptions.SectionName}:OtlpEndpoint must be an absolute URI when configured.")
    .ValidateOnStart();

var swaggerEnabled = builder.Environment.IsDevelopment() || configuredOpenApiOptions.EnableSwaggerInNonDevelopment;
var jwtSigningKeyBytes = Encoding.UTF8.GetBytes(configuredJwtOptions.SigningKey ?? string.Empty);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = configuredJwtOptions.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtSigningKeyBytes),
            ValidateIssuer = true,
            ValidIssuer = configuredJwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = configuredJwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("trading.write", policy =>
        policy.RequireAuthenticatedUser().RequireRole("admin", "trader"));

    options.AddPolicy("trading.read", policy =>
        policy.RequireAuthenticatedUser().RequireRole("admin", "trader", "operator"));

    options.AddPolicy("operations.read", policy =>
        policy.RequireAuthenticatedUser().RequireRole("admin", "operator"));

    options.AddPolicy("integration.write", policy =>
        policy.RequireAuthenticatedUser().RequireRole("admin", "integration"));
});

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
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments("/health/live");
                options.EnrichWithHttpRequest = (activity, request) =>
                {
                    if (request.Headers.TryGetValue(RequestMetadata.CorrelationIdHeaderName, out var correlationId))
                    {
                        activity.SetTag("kalshi.correlation_id", correlationId.ToString());
                    }
                };
                options.EnrichWithHttpResponse = (activity, response) =>
                {
                    activity.SetTag("kalshi.environment", builder.Environment.EnvironmentName);
                };
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
                options.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    if (request.Headers.TryGetValues(RequestMetadata.CorrelationIdHeaderName, out var correlationValues))
                    {
                        activity.SetTag("kalshi.correlation_id", correlationValues.FirstOrDefault());
                    }
                };
            })
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
                options.SetDbStatementForStoredProcedure = true;
            });

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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("x-api-version"));
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Kalshi Integration Event Publisher API",
        Version = "v1",
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme.ToLowerInvariant(),
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT bearer token. Example: 'Bearer {token}'",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = JwtBearerDefaults.AuthenticationScheme,
        },
    };

    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [securityScheme] = Array.Empty<string>(),
    });
});

var app = builder.Build();
var databaseOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
var applyMigrationsOnStartup = databaseOptions.ApplyMigrationsOnStartup && !app.Environment.IsEnvironment("Testing");

if (applyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<KalshiIntegrationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseMiddleware<RequestTimingMiddleware>();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/dashboard", () => Results.Redirect("/dashboard/index.html"))
    .AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync,
})
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync,
})
    .AllowAnonymous();

app.MapGet("/", () => Results.Ok(new
{
    application = "Kalshi Integration Event Publisher",
    environment = app.Environment.EnvironmentName,
    version = "v1"
}))
    .AllowAnonymous();

app.Run();

/// <summary>
/// Exposes the application entry point for integration tests.
/// </summary>
public partial class Program;
