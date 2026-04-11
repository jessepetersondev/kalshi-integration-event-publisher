using Kalshi.Integration.Infrastructure.Messaging;
using RabbitMQ.Client.Exceptions;

namespace Kalshi.Integration.Executor.Execution;

public sealed class ExecutionReliabilityPolicy
{
    public bool IsRetryable(Exception exception)
    {
        return exception switch
        {
            PublishConfirmationException publishFailure => publishFailure.IsRetryable,
            RetryableExecutionException => true,
            TimeoutException => true,
            HttpRequestException => true,
            BrokerUnreachableException => true,
            IOException => true,
            OperationCanceledException => false,
            _ => false,
        };
    }

    public TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        var exponent = Math.Max(0, attemptNumber - 1);
        var baseDelayMs = Math.Min(30000, 500 * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(baseDelayMs + Random.Shared.Next(0, 251));
    }
}
