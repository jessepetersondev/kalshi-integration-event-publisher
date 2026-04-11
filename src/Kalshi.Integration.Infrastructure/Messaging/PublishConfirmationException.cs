namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Raised when RabbitMQ publication cannot be confirmed.
/// </summary>
public sealed class PublishConfirmationException(
    string message,
    RabbitMqPublishFailureKind failureKind = RabbitMqPublishFailureKind.Unknown,
    bool isRetryable = true,
    Exception? innerException = null) : Exception(message, innerException)
{
    public RabbitMqPublishFailureKind FailureKind { get; } = failureKind;

    public bool IsRetryable { get; } = isRetryable;
}
