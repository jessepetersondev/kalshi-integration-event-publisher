namespace Kalshi.Integration.Executor.Routing;

public static class ExecutorEventNames
{
    public const string TradeIntentCreated = "trade-intent.created";
    public const string OrderCreated = "order.created";
    public const string ExecutionUpdateApplied = "execution-update.applied";

    public static bool TryGetResultEventName(string eventName, out string resultEventName)
    {
        resultEventName = eventName switch
        {
            TradeIntentCreated => "trade-intent.executed",
            OrderCreated => "order.execution_succeeded",
            ExecutionUpdateApplied => "execution-update.reconciled",
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(resultEventName);
    }
}
