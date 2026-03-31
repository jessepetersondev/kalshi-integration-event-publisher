namespace Kalshi.Integration.Application.Risk;

/// <summary>
/// Represents the outcome of risk evaluation.
/// </summary>


public sealed record RiskDecision(
    bool Accepted,
    string Decision,
    IReadOnlyList<string> Reasons,
    int MaxOrderSize,
    bool DuplicateCorrelationIdDetected);
