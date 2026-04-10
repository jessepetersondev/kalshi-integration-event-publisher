using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Kalshi.Integration.AcceptanceTests;

public sealed class AcceptanceTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string JwtIssuer = "kalshi-integration-event-publisher";
    private const string JwtAudience = "kalshi-integration-event-publisher-clients";
    private const string JwtSigningKey = "kalshi-integration-event-publisher-local-dev-signing-key-please-change";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "kalshi-integration-event-publisher", "acceptance", $"{Guid.NewGuid():N}.db");
    private readonly object _databaseInitializationLock = new();
    private bool _databaseInitialized;

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
                ["Authentication:Jwt:Issuer"] = JwtIssuer,
                ["Authentication:Jwt:Audience"] = JwtAudience,
                ["Authentication:Jwt:SigningKey"] = JwtSigningKey,
                ["Authentication:Jwt:EnableDevelopmentTokenIssuance"] = "true",
                ["OpenApi:EnableSwaggerInNonDevelopment"] = "false",
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_databaseInitializationLock)
        {
            if (!_databaseInitialized)
            {
                EnsureDatabaseDirectory();
                TryDeleteDatabase();
            }
        }

        var host = base.CreateHost(builder);

        lock (_databaseInitializationLock)
        {
            if (_databaseInitialized)
            {
                return host;
            }

            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<KalshiIntegrationDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();
            _databaseInitialized = true;
        }

        return host;
    }

    public HttpClient CreateAuthenticatedClient(params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken(roles));
        return client;
    }

    public static string CreateJwtToken(params string[] roles)
    {
        var normalizedRoles = roles is { Length: > 0 }
            ? roles.Select(role => role.Trim()).Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal).ToArray()
            : ["admin"];

        var now = DateTimeOffset.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtIssuer,
            Audience = JwtAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddHours(1).UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "acceptance-test-user"),
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
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void TryDeleteDatabase()
    {
        try
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
