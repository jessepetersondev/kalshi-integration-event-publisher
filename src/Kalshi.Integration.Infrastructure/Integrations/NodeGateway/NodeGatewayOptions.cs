using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

public sealed class NodeGatewayOptions
{
    public const string SectionName = "Integrations:NodeGateway";

    public bool Enabled { get; set; } = true;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:3001";

    [Required]
    public string HealthPath { get; set; } = "/health";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;

    [Range(0, 5)]
    public int RetryAttempts { get; set; } = 2;

    public bool IncludeInReadiness { get; set; }
}
