namespace Kalshi.Integration.Executor.Messaging.Consumers;

public interface IExecutorMessagePump
{
    Task RunAsync(CancellationToken cancellationToken);
}
