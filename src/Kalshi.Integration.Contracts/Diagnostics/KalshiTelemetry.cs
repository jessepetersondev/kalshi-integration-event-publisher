using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kalshi.Integration.Contracts.Diagnostics;

/// <summary>
/// Defines telemetry names and instruments for kalshi.
/// </summary>


public static class KalshiTelemetry
{
    public const string ActivitySourceName = "Kalshi.Integration";
    public const string MeterName = "Kalshi.Integration";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> HttpServerRequestDurationMs = Meter.CreateHistogram<double>(
        "kalshi.http.server.request.duration",
        unit: "ms",
        description: "Duration of inbound HTTP requests handled by the API.");

    public static readonly Histogram<double> OutboundDependencyDurationMs = Meter.CreateHistogram<double>(
        "kalshi.dependency.call.duration",
        unit: "ms",
        description: "Duration of outbound dependency calls issued by the application.");
}
