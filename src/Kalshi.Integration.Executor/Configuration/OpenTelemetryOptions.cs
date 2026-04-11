using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Executor.Configuration;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    [Required]
    public string ServiceName { get; set; } = "Kalshi.Integration.Executor";

    public string? ServiceVersion { get; set; }

    public string? OtlpEndpoint { get; set; }
}
