using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Configures OpenTelemetry enrichment and exporter behavior for the API.
/// </summary>
public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Gets the configuration section name for OpenTelemetry settings.
    /// </summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Gets or sets the service name emitted with traces and metrics.
    /// </summary>
    [Required]
    public string ServiceName { get; set; } = "Kalshi.Integration.Api";

    /// <summary>
    /// Gets or sets the service version emitted with telemetry.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Gets or sets the OTLP endpoint used for trace and metric export.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the Azure Monitor connection string used for exporter configuration.
    /// </summary>
    public string? AzureMonitorConnectionString { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the console exporter is enabled.
    /// </summary>
    public bool EnableConsoleExporter { get; set; }
}
