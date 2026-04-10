using Kalshi.Integration.Domain.Common;

namespace Kalshi.Integration.Domain.TradeIntents;

/// <summary>
/// Represents the domain model for trade intent.
/// </summary>
public sealed class TradeIntent
{
    private const string LegacyOriginService = "legacy-client";
    private const string LegacyDecisionReason = "legacy request";
    private const string LegacyCommandSchemaVersion = "weather-quant-command.v1";

    public TradeIntent(
        string ticker,
        TradeSide? side,
        int? quantity,
        decimal? limitPrice,
        string strategyName)
        : this(
            ticker,
            side,
            quantity,
            limitPrice,
            strategyName,
            TradeIntentActionType.Entry,
            LegacyOriginService,
            LegacyDecisionReason,
            LegacyCommandSchemaVersion)
    {
    }

    public TradeIntent(
        string ticker,
        TradeSide? side,
        int? quantity,
        decimal? limitPrice,
        string strategyName,
        string? correlationId,
        DateTimeOffset? createdAt = null)
        : this(
            ticker,
            side,
            quantity,
            limitPrice,
            strategyName,
            TradeIntentActionType.Entry,
            LegacyOriginService,
            LegacyDecisionReason,
            LegacyCommandSchemaVersion,
            correlationId: correlationId,
            createdAt: createdAt)
    {
    }

    public TradeIntent(
        string ticker,
        TradeSide? side,
        int? quantity,
        decimal? limitPrice,
        string strategyName,
        TradeIntentActionType actionType,
        string originService,
        string decisionReason,
        string commandSchemaVersion,
        string? targetPositionTicker = null,
        TradeSide? targetPositionSide = null,
        Guid? targetPublisherOrderId = null,
        string? targetClientOrderId = null,
        string? targetExternalOrderId = null,
        string? correlationId = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new DomainException("Ticker is required.");
        }

        if (string.IsNullOrWhiteSpace(strategyName))
        {
            throw new DomainException("Strategy name is required.");
        }

        if (string.IsNullOrWhiteSpace(originService))
        {
            throw new DomainException("Origin service is required.");
        }

        if (string.IsNullOrWhiteSpace(decisionReason))
        {
            throw new DomainException("Decision reason is required.");
        }

        if (string.IsNullOrWhiteSpace(commandSchemaVersion))
        {
            throw new DomainException("Command schema version is required.");
        }

        ValidateActionSpecificFields(
            actionType,
            side,
            quantity,
            limitPrice,
            targetPositionTicker,
            targetPositionSide,
            targetPublisherOrderId,
            targetClientOrderId,
            targetExternalOrderId);

        Id = Guid.NewGuid();
        Ticker = ticker.Trim().ToUpperInvariant();
        Side = side;
        Quantity = quantity;
        LimitPrice = limitPrice.HasValue
            ? decimal.Round(limitPrice.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        StrategyName = strategyName.Trim();
        ActionType = actionType;
        OriginService = originService.Trim();
        DecisionReason = decisionReason.Trim();
        CommandSchemaVersion = commandSchemaVersion.Trim();
        TargetPositionTicker = string.IsNullOrWhiteSpace(targetPositionTicker) ? null : targetPositionTicker.Trim().ToUpperInvariant();
        TargetPositionSide = targetPositionSide;
        TargetPublisherOrderId = targetPublisherOrderId;
        TargetClientOrderId = string.IsNullOrWhiteSpace(targetClientOrderId) ? null : targetClientOrderId.Trim();
        TargetExternalOrderId = string.IsNullOrWhiteSpace(targetExternalOrderId) ? null : targetExternalOrderId.Trim();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Id.ToString("N") : correlationId.Trim();
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Ticker { get; }
    public TradeSide? Side { get; }
    public int? Quantity { get; }
    public decimal? LimitPrice { get; }
    public string StrategyName { get; }
    public TradeIntentActionType ActionType { get; }
    public string OriginService { get; }
    public string DecisionReason { get; }
    public string CommandSchemaVersion { get; }
    public string? TargetPositionTicker { get; }
    public TradeSide? TargetPositionSide { get; }
    public Guid? TargetPublisherOrderId { get; }
    public string? TargetClientOrderId { get; }
    public string? TargetExternalOrderId { get; }
    public string CorrelationId { get; }
    public DateTimeOffset CreatedAt { get; }

    public TradeIntent WithId(Guid id)
    {
        Id = id;
        return this;
    }

    private static void ValidateActionSpecificFields(
        TradeIntentActionType actionType,
        TradeSide? side,
        int? quantity,
        decimal? limitPrice,
        string? targetPositionTicker,
        TradeSide? targetPositionSide,
        Guid? targetPublisherOrderId,
        string? targetClientOrderId,
        string? targetExternalOrderId)
    {
        static bool HasPositiveQuantity(int? value) => value.HasValue && value.Value > 0;
        static bool HasValidLimitPrice(decimal? value) => value.HasValue && value.Value > 0m && value.Value <= 1m;

        if (actionType is TradeIntentActionType.Entry or TradeIntentActionType.Exit)
        {
            if (side is null)
            {
                throw new DomainException("Side is required for entry and exit actions.");
            }

            if (!HasPositiveQuantity(quantity))
            {
                throw new DomainException("Quantity must be greater than zero for entry and exit actions.");
            }

            if (!HasValidLimitPrice(limitPrice))
            {
                throw new DomainException("Limit price must be greater than 0 and less than or equal to 1 for entry and exit actions.");
            }
        }

        if (actionType == TradeIntentActionType.Exit)
        {
            if (string.IsNullOrWhiteSpace(targetPositionTicker) || targetPositionSide is null)
            {
                throw new DomainException("Exit actions require target position ticker and side.");
            }
        }

        if (actionType == TradeIntentActionType.Cancel)
        {
            if (targetPublisherOrderId is null && string.IsNullOrWhiteSpace(targetClientOrderId) && string.IsNullOrWhiteSpace(targetExternalOrderId))
            {
                throw new DomainException("Cancel actions require at least one target order reference.");
            }
        }
    }
}
