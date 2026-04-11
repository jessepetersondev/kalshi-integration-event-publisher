namespace Kalshi.Integration.Contracts.Reliability;

/// <summary>
/// Represents the durable outbox state for a queued outbound message.
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 1,
    InFlight = 2,
    Published = 3,
    ManualInterventionRequired = 4,
}
