namespace Kalshi.Integration.Contracts.TradeIntents;

/// <summary>
/// Represents a response payload for risk decision.
/// </summary>


public sealed record RiskDecisionResponse(
    bool Accepted,
    string Decision,
    string[] Reasons,
    int MaxOrderSize,
    bool DuplicateCorrelationIdDetected);
