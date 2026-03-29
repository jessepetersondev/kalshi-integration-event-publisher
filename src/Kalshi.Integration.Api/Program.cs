using System.Text;
using Asp.Versioning;
using Kalshi.Integration.Api.Configuration;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application;
using Kalshi.Integration.Infrastructure;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

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

builder.Services.AddOptions<OpenApiOptions>()
    .Bind(builder.Configuration.GetSection(OpenApiOptions.SectionName))
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
        Title = "Kalshi Integration Sandbox API",
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
var applyMigrationsOnStartup = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup;

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
    application = "Kalshi Integration Sandbox",
    environment = app.Environment.EnvironmentName,
    version = "v1"
}))
    .AllowAnonymous();

app.Run();

public partial class Program;
