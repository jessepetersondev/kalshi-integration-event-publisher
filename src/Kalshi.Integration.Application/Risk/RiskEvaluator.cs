using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.TradeIntents;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Application.Risk;

/// <summary>
/// Applies the publisher's configurable validation and duplicate-detection rules to trade intents.
/// </summary>
public sealed class RiskEvaluator
{
    private readonly ITradeIntentRepository _tradeIntentRepository;
    private readonly RiskOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RiskEvaluator"/> class.
    /// </summary>
    /// <param name="tradeIntentRepository">The repository used to detect duplicate correlation identifiers.</param>
    /// <param name="options">The configured risk limits.</param>
    public RiskEvaluator(ITradeIntentRepository tradeIntentRepository, IOptions<RiskOptions> options)
    {
        _tradeIntentRepository = tradeIntentRepository;
        _options = options.Value;
    }

    public async Task<RiskDecision> EvaluateTradeIntentAsync(CreateTradeIntentRequest request, CancellationToken cancellationToken = default)
    {
        var reasons = new List<string>();
        var duplicateDetected = false;

        if (string.IsNullOrWhiteSpace(request.Ticker))
        {
            reasons.Add("Ticker is required.");
        }

        if (!string.Equals(request.Side, "yes", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Side, "no", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(request.Side))
        {
            reasons.Add("Side must be either 'yes' or 'no'.");
        }

        if (request.Quantity.HasValue && request.Quantity.Value <= 0)
        {
            reasons.Add("Quantity must be greater than zero.");
        }

        if (request.Quantity.HasValue && request.Quantity.Value > _options.MaxOrderSize)
        {
            reasons.Add($"Quantity exceeds max order size of {_options.MaxOrderSize}.");
        }

        if (request.LimitPrice.HasValue && (request.LimitPrice.Value <= 0m || request.LimitPrice.Value > 1m))
        {
            reasons.Add("Limit price must be greater than 0 and less than or equal to 1.");
        }

        if (string.IsNullOrWhiteSpace(request.StrategyName))
        {
            reasons.Add("Strategy name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ActionType) || !Enum.TryParse<TradeIntentActionType>(request.ActionType, ignoreCase: true, out var actionType))
        {
            reasons.Add("Action type must be one of 'entry', 'exit', or 'cancel'.");
        }
        else
        {
            if (actionType is TradeIntentActionType.Entry or TradeIntentActionType.Exit)
            {
                if (string.IsNullOrWhiteSpace(request.Side))
                {
                    reasons.Add("Side is required for entry and exit actions.");
                }

                if (!request.Quantity.HasValue || request.Quantity.Value <= 0)
                {
                    reasons.Add("Quantity is required for entry and exit actions.");
                }

                if (!request.LimitPrice.HasValue || request.LimitPrice.Value <= 0m || request.LimitPrice.Value > 1m)
                {
                    reasons.Add("Limit price is required for entry and exit actions.");
                }
            }

            if (actionType == TradeIntentActionType.Exit)
            {
                if (string.IsNullOrWhiteSpace(request.TargetPositionTicker) || string.IsNullOrWhiteSpace(request.TargetPositionSide))
                {
                    reasons.Add("Exit actions require target position ticker and side.");
                }
            }

            if (actionType == TradeIntentActionType.Cancel &&
                request.TargetPublisherOrderId is null &&
                string.IsNullOrWhiteSpace(request.TargetClientOrderId) &&
                string.IsNullOrWhiteSpace(request.TargetExternalOrderId))
            {
                reasons.Add("Cancel actions require at least one target order reference.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.OriginService))
        {
            reasons.Add("Origin service is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DecisionReason))
        {
            reasons.Add("Decision reason is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CommandSchemaVersion))
        {
            reasons.Add("Command schema version is required.");
        }

        if (_options.RejectDuplicateCorrelationIds && !string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            var existing = await _tradeIntentRepository.GetTradeIntentByCorrelationIdAsync(request.CorrelationId.Trim(), cancellationToken);
            if (existing is not null)
            {
                duplicateDetected = true;
                reasons.Add($"Correlation id '{request.CorrelationId}' has already been used.");
            }
        }

        var accepted = reasons.Count == 0;
        return new RiskDecision(
            accepted,
            accepted ? "accepted" : "rejected",
            reasons,
            _options.MaxOrderSize,
            duplicateDetected);
    }
}
