using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kalshi.Integration.Contracts.Diagnostics;

/// <summary>
/// Defines telemetry names and instruments for kalshi.
/// </summary>
public static class KalshiTelemetry
{
    /// <summary>
    /// Gets the activity source name used by Kalshi integration traces.
    /// </summary>
    public const string ActivitySourceName = "Kalshi.Integration";

    /// <summary>
    /// Gets the meter name used by Kalshi integration metrics.
    /// </summary>
    public const string MeterName = "Kalshi.Integration";

    /// <summary>
    /// Gets the activity source used to create Kalshi integration trace spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Gets the meter used to publish Kalshi integration metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Gets the histogram that records inbound HTTP server request duration in milliseconds.
    /// </summary>
    public static readonly Histogram<double> HttpServerRequestDurationMs = Meter.CreateHistogram<double>(
        "kalshi.http.server.request.duration",
        unit: "ms",
        description: "Duration of inbound HTTP requests handled by the API.");

    /// <summary>
    /// Gets the histogram that records outbound dependency-call duration in milliseconds.
    /// </summary>
    public static readonly Histogram<double> OutboundDependencyDurationMs = Meter.CreateHistogram<double>(
        "kalshi.dependency.call.duration",
        unit: "ms",
        description: "Duration of outbound dependency calls issued by the application.");
}
