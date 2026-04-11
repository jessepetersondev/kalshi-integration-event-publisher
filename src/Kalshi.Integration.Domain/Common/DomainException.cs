namespace Kalshi.Integration.Domain.Common;

/// <summary>
/// Represents an error related to domain.
/// </summary>
public sealed class DomainException(string message) : Exception(message)
{
}
