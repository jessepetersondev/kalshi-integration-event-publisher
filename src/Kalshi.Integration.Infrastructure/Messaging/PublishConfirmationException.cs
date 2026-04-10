namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Raised when RabbitMQ publication cannot be confirmed.
/// </summary>
public sealed class PublishConfirmationException : Exception
{
    public PublishConfirmationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
