namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Describes the broker-side reason a publish attempt failed.
/// </summary>
public enum RabbitMqPublishFailureKind
{
    Unknown = 0,
    ConfirmTimeout = 1,
    Nack = 2,
    Unroutable = 3,
    ConnectionInterrupted = 4,
    ChannelClosed = 5,
    BrokerUnavailable = 6,
    Configuration = 7,
}
