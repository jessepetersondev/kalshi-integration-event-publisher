using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Configures OpenTelemetry enrichment and exporter behavior for the API.
/// </summary>
public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    [Required]
    public string ServiceName { get; set; } = "Kalshi.Integration.Api";

    public string? ServiceVersion { get; set; }

    public string? OtlpEndpoint { get; set; }

    public string? AzureMonitorConnectionString { get; set; }

    public bool EnableConsoleExporter { get; set; }
}
