namespace Kalshi.Integration.Domain.Common;

/// <summary>
/// Represents an error related to domain.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
