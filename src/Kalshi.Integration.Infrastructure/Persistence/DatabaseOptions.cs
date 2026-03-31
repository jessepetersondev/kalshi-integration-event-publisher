using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Represents configuration for database.
/// </summary>


public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
/// <summary>
/// Defines supported database values.
/// </summary>


public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";

    public static string Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return Sqlite;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlite" => Sqlite,
            "sqlserver" => SqlServer,
            "sql_server" => SqlServer,
            "mssql" => SqlServer,
            "azuresql" => SqlServer,
            "azure_sql" => SqlServer,
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'. Supported providers: {Sqlite}, {SqlServer}.")
        };
    }

    public static void EnsureConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'KalshiIntegration' is required.");
        }
    }

    public static string GetDependencyName(string? efProviderName)
    {
        if (string.IsNullOrWhiteSpace(efProviderName))
        {
            return "database";
        }

        if (efProviderName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return "sqlserver";
        }

        if (efProviderName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return "sqlite";
        }

        return "database";
    }

    public static string GetHealthDescription(string? efProviderName, bool canConnect)
    {
        var provider = GetDependencyName(efProviderName);
        var providerLabel = provider switch
        {
            "sqlserver" => "SQL Server/Azure SQL",
            "sqlite" => "SQLite",
            _ => "database"
        };

        return canConnect
            ? $"{providerLabel} connectivity verified."
            : $"{providerLabel} connectivity check failed.";
    }
}
