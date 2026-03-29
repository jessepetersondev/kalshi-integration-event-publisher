using Kalshi.Integration.Executor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Messaging.Consumers;

public sealed class RabbitMqConsumerShell : IExecutorMessagePump
{
    private readonly ExecutorOptions _executorOptions;
    private readonly ILogger<RabbitMqConsumerShell> _logger;
    private readonly RabbitMqOptions _rabbitMqOptions;

    public RabbitMqConsumerShell(
        IOptions<ExecutorOptions> executorOptions,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<RabbitMqConsumerShell> logger)
    {
        _executorOptions = executorOptions.Value;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executor shell is configured for exchange {Exchange}, queue {Queue}, result queue {ResultQueue}, dead-letter queue {DeadLetterQueue}, and bindings {Bindings}.",
            _rabbitMqOptions.Exchange,
            _executorOptions.PrimaryQueue,
            _executorOptions.ResultQueue,
            _executorOptions.DeadLetterQueue,
            string.Join(", ", _executorOptions.RoutingBindings));

        return Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
