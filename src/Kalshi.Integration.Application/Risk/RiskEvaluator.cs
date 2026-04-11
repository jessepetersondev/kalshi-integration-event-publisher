using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.TradeIntents;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Application.Risk;

/// <summary>
/// Applies the publisher's configurable validation and duplicate-detection rules to trade intents.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RiskEvaluator"/> class.
/// </remarks>
/// <param name="tradeIntentRepository">The repository used to detect duplicate correlation identifiers.</param>
/// <param name="options">The configured risk limits.</param>
public sealed class RiskEvaluator(ITradeIntentRepository tradeIntentRepository, IOptions<RiskOptions> options)
{
    private readonly ITradeIntentRepository _tradeIntentRepository = tradeIntentRepository;
    private readonly RiskOptions _options = options.Value;

    public async Task<RiskDecision> EvaluateTradeIntentAsync(CreateTradeIntentRequest request, CancellationToken cancellationToken = default)
    {
        List<string> reasons = [];
        bool duplicateDetected = false;

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

        if (string.IsNullOrWhiteSpace(request.ActionType) || !Enum.TryParse<TradeIntentActionType>(request.ActionType, ignoreCase: true, out TradeIntentActionType actionType))
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

            if (actionType == TradeIntentActionType.Cancel)
            {
                TradeIntent? existingCancel = await _tradeIntentRepository.FindMatchingCancelTradeIntentAsync(
                    request.TargetPublisherOrderId,
                    request.TargetClientOrderId?.Trim(),
                    request.TargetExternalOrderId?.Trim(),
                    cancellationToken);

                if (existingCancel is not null)
                {
                    reasons.Add("A cancel request already exists for the target order.");
                }
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
            TradeIntent? existing = await _tradeIntentRepository.GetTradeIntentByCorrelationIdAsync(request.CorrelationId.Trim(), cancellationToken);
            if (existing is not null)
            {
                duplicateDetected = true;
                reasons.Add($"Correlation id '{request.CorrelationId}' has already been used.");
            }
        }

        bool accepted = reasons.Count == 0;
        return new RiskDecision(
            accepted,
            accepted ? "accepted" : "rejected",
            reasons,
            _options.MaxOrderSize,
            duplicateDetected);
    }
}
