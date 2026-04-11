namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExecutionRecordEntity
{
    public Guid Id { get; set; }
    public Guid PublisherOrderId { get; set; }
    public Guid TradeIntentId { get; set; }
    public Guid CommandEventId { get; set; }
    public Guid? LastSourceEventId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? Side { get; set; }
    public int? Quantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string ClientOrderId { get; set; } = string.Empty;
    public string? ExternalOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastError { get; set; }
    public string? LastResultEventName { get; set; }
    public DateTimeOffset? TerminalResultQueuedAt { get; set; }
    public DateTimeOffset? TerminalResultPublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
