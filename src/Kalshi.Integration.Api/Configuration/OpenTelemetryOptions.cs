using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

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
