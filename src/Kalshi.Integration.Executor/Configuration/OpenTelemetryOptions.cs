using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Executor.Configuration;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    [Required]
    public string ServiceName { get; set; } = "Kalshi.Integration.Executor";

    [Required]
    public string ServiceVersion { get; set; } = "v1";

    public string? OtlpEndpoint { get; set; }
}
