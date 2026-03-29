using System.Diagnostics.Metrics;
using Kalshi.Integration.Contracts.Diagnostics;

namespace Kalshi.Integration.Executor.Observability;

public static class ExecutorTelemetry
{
    public static readonly Counter<long> MessagesHandled = KalshiTelemetry.Meter.CreateCounter<long>(
        "kalshi.executor.messages.handled",
        description: "Count of inbound executor events handled successfully.");

    public static readonly Counter<long> MessagesSkipped = KalshiTelemetry.Meter.CreateCounter<long>(
        "kalshi.executor.messages.skipped",
        description: "Count of duplicate or ignored executor events.");

    public static readonly Counter<long> ResultEventsPublished = KalshiTelemetry.Meter.CreateCounter<long>(
        "kalshi.executor.result_events.published",
        description: "Count of executor result events emitted by the worker shell.");
}
