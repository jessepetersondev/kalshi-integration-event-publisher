namespace Kalshi.Integration.Executor.Execution;

/// <summary>
/// Marks an executor failure as safe to retry without producing a terminal failure event.
/// </summary>
public sealed class RetryableExecutionException : Exception
{
    public RetryableExecutionException(string message)
        : base(message)
    {
    }

    public RetryableExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
