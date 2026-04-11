using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Kalshi.Integration.IntegrationTests;

public sealed class IntegrationTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string JwtIssuer = "kalshi-integration-event-publisher";
    private const string JwtAudience = "kalshi-integration-event-publisher-clients";
    private const string JwtSigningKey = "kalshi-integration-event-publisher-local-dev-signing-key-please-change";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "kalshi-integration-event-publisher", "integration", $"{Guid.NewGuid():N}.db");
    private readonly object _databaseInitializationLock = new();
    private bool _databasePrepared;
    private IHost? _host;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = $"Data Source={_databasePath}",
                ["Database:Provider"] = "Sqlite",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Database__ApplyMigrationsOnStartup"] = "false",
                ["EventPublishing:Provider"] = "InMemory",
                ["EventPublishing__Provider"] = "InMemory",
                ["RabbitMq:EnableResultConsumer"] = "false",
                ["RabbitMq__EnableResultConsumer"] = "false",
                ["RabbitMq:EnableReliabilityMonitoring"] = "false",
                ["RabbitMq__EnableReliabilityMonitoring"] = "false",
                ["RabbitMq:EnableQueueHealthChecks"] = "false",
                ["RabbitMq__EnableQueueHealthChecks"] = "false",
                ["Authentication:Jwt:Issuer"] = JwtIssuer,
                ["Authentication:Jwt:Audience"] = JwtAudience,
                ["Authentication:Jwt:SigningKey"] = JwtSigningKey,
                ["Authentication:Jwt:EnableDevelopmentTokenIssuance"] = "true",
                ["OpenApi:EnableSwaggerInNonDevelopment"] = "false",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<DatabaseOptions>(options =>
            {
                options.Provider = DatabaseProviders.Sqlite;
                options.ApplyMigrationsOnStartup = false;
            });
            services.PostConfigure<EventPublisherOptions>(options =>
            {
                options.Provider = EventPublisherProviders.InMemory;
            });
            services.PostConfigure<RabbitMqOptions>(options =>
            {
                options.EnableResultConsumer = false;
                options.EnableReliabilityMonitoring = false;
                options.EnableQueueHealthChecks = false;
            });
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> registrationsToRemove = options.Registrations
                    .Where(registration => string.Equals(registration.Name, "rabbitmq-queues", StringComparison.Ordinal))
                    .ToList();

                foreach (HealthCheckRegistration registration in registrationsToRemove)
                {
                    options.Registrations.Remove(registration);
                }
            });

            RemoveHostedService<PublisherReliabilityMonitorBackgroundService>(services);
            RemoveHostedService<RabbitMqResultEventConsumer>(services);

            services.RemoveAll<IDbContextFactory<KalshiIntegrationDbContext>>();
            services.RemoveAll<KalshiIntegrationDbContext>();
            services.RemoveAll<DbContextOptions<KalshiIntegrationDbContext>>();

            services.AddSingleton<IDbContextFactory<KalshiIntegrationDbContext>>(_ => new TestKalshiIntegrationDbContextFactory(_databasePath));
            services.AddScoped(serviceProvider => serviceProvider.GetRequiredService<IDbContextFactory<KalshiIntegrationDbContext>>().CreateDbContext());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_databaseInitializationLock)
        {
            if (_host is not null)
            {
                return _host;
            }

            if (!_databasePrepared)
            {
                EnsureDatabaseDirectory();
                TryDeleteDatabase();
                MigrateDatabase();
                _databasePrepared = true;
            }

            _host = base.CreateHost(builder);
            return _host;
        }
    }

    public HttpClient CreateAuthenticatedClient(params string[] roles)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken(roles));
        return client;
    }

    public static string CreateJwtToken(params string[] roles)
    {
        string[] normalizedRoles = roles is { Length: > 0 }
            ? roles.Select(role => role.Trim()).Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal).ToArray()
            : ["admin"];

        DateTimeOffset now = DateTimeOffset.UtcNow;
        JwtSecurityTokenHandler handler = new();
        SecurityToken token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtIssuer,
            Audience = JwtAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddHours(1).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "integration-test-user"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                .. normalizedRoles.Select(role => new Claim(ClaimTypes.Role, role)),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
                SecurityAlgorithms.HmacSha256Signature),
        });

        return handler.WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        TryDeleteDatabase();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TryDeleteDatabase();
    }

    private void EnsureDatabaseDirectory()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void MigrateDatabase()
    {
        DbContextOptions<KalshiIntegrationDbContext> options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        using KalshiIntegrationDbContext dbContext = new(options);
        dbContext.Database.Migrate();
    }

    private void TryDeleteDatabase()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            string walPath = $"{_databasePath}-wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }

            string shmPath = $"{_databasePath}-shm";
            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : class, IHostedService
    {
        ServiceDescriptor? descriptor = services
            .LastOrDefault(entry => entry.ServiceType == typeof(IHostedService) && entry.ImplementationType == typeof(THostedService));

        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }

    private sealed class TestKalshiIntegrationDbContextFactory(string databasePath) : IDbContextFactory<KalshiIntegrationDbContext>
    {
        private readonly string _databasePath = databasePath;

        public KalshiIntegrationDbContext CreateDbContext()
        {
            DbContextOptions<KalshiIntegrationDbContext> options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .Options;

            return new KalshiIntegrationDbContext(options);
        }
    }
}
