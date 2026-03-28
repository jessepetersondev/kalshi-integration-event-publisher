namespace Kalshi.Integration.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "kalshi.integration.events";
    public string ExchangeType { get; set; } = "topic";
    public string RoutingKeyPrefix { get; set; } = "kalshi.integration";
    public bool Mandatory { get; set; }
    public string ClientProvidedName { get; set; } = "kalshi-integration-sandbox";
}
