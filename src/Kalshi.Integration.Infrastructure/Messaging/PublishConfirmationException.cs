namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Raised when RabbitMQ publication cannot be confirmed.
/// </summary>
public sealed class PublishConfirmationException : Exception
{
    public PublishConfirmationException(
        string message,
        RabbitMqPublishFailureKind failureKind = RabbitMqPublishFailureKind.Unknown,
        bool isRetryable = true,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        IsRetryable = isRetryable;
    }

    public RabbitMqPublishFailureKind FailureKind { get; }

    public bool IsRetryable { get; }
}
