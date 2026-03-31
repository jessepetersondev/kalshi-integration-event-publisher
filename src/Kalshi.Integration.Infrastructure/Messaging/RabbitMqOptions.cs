using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Configures the RabbitMQ connectivity and topology used for published events.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string VirtualHost { get; set; } = "/";

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    [Required]
    public string Exchange { get; set; } = "kalshi.integration.events";

    [Required]
    public string ExchangeType { get; set; } = "topic";

    [Required]
    public string RoutingKeyPrefix { get; set; } = "kalshi.integration";

    public bool Mandatory { get; set; }

    [Required]
    public string ClientProvidedName { get; set; } = "kalshi-integration-event-publisher";
}
