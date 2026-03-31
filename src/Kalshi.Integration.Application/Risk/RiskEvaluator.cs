using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.TradeIntents;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Application.Risk;

/// <summary>
/// Represents risk evaluator.
/// </summary>


public sealed class RiskEvaluator
{
    private readonly ITradeIntentRepository _tradeIntentRepository;
    private readonly RiskOptions _options;

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
            !string.Equals(request.Side, "no", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Side must be either 'yes' or 'no'.");
        }

        if (request.Quantity <= 0)
        {
            reasons.Add("Quantity must be greater than zero.");
        }

        if (request.Quantity > _options.MaxOrderSize)
        {
            reasons.Add($"Quantity exceeds max order size of {_options.MaxOrderSize}.");
        }

        if (request.LimitPrice <= 0m || request.LimitPrice > 1m)
        {
            reasons.Add("Limit price must be greater than 0 and less than or equal to 1.");
        }

        if (string.IsNullOrWhiteSpace(request.StrategyName))
        {
            reasons.Add("Strategy name is required.");
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
