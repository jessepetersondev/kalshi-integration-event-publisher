namespace Kalshi.Integration.Application.Risk;

/// <summary>
/// Captures the outcome of evaluating a trade intent against configured risk rules.
/// </summary>
public sealed record RiskDecision(
    bool Accepted,
    string Decision,
    IReadOnlyList<string> Reasons,
    int MaxOrderSize,
    bool DuplicateCorrelationIdDetected);
