namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for trade intent.
/// </summary>
public sealed class TradeIntentEntity
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string? Side { get; set; }
    public int? Quantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string OriginService { get; set; } = string.Empty;
    public string DecisionReason { get; set; } = string.Empty;
    public string CommandSchemaVersion { get; set; } = string.Empty;
    public string? TargetPositionTicker { get; set; }
    public string? TargetPositionSide { get; set; }
    public Guid? TargetPublisherOrderId { get; set; }
    public string? TargetClientOrderId { get; set; }
    public string? TargetExternalOrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
