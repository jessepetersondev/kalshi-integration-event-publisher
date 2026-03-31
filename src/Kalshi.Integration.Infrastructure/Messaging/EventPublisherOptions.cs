using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Configures the application-event publisher used by the publisher service.
/// </summary>
public sealed class EventPublisherOptions
{
    public const string SectionName = "EventPublishing";

    [Required]
    public string Provider { get; set; } = EventPublisherProviders.InMemory;
}

/// <summary>
/// Defines supported event publisher values.
/// </summary>
public static class EventPublisherProviders
{
    public const string InMemory = "InMemory";
    public const string RabbitMq = "RabbitMq";

    public static string Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return InMemory;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "inmemory" => InMemory,
            "rabbitmq" => RabbitMq,
            "rabbit_mq" => RabbitMq,
            _ => throw new InvalidOperationException($"Unsupported event publishing provider '{provider}'. Supported providers: {InMemory}, {RabbitMq}.")
        };
    }
}
