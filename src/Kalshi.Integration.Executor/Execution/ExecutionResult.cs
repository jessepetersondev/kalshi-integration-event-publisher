namespace Kalshi.Integration.Executor.Execution;

public sealed record ExecutionResult(bool Succeeded, string ResultEventName)
{
    public static ExecutionResult Success(string resultEventName) => new(true, resultEventName);
}
