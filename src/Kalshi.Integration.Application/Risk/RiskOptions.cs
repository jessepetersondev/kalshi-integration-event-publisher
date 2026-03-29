using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Application.Risk;

public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    [Range(1, 1000)]
    public int MaxOrderSize { get; set; } = 10;

    public bool RejectDuplicateCorrelationIds { get; set; } = true;
}
