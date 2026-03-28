namespace Kalshi.Integration.Infrastructure.Messaging;

public sealed class EventPublisherOptions
{
    public const string SectionName = "EventPublishing";

    public string Provider { get; set; } = EventPublisherProviders.InMemory;
}

public static class EventPublisherProviders
{
    public const string InMemory = "InMemory";
    public const string RabbitMq = "RabbitMq";
}
