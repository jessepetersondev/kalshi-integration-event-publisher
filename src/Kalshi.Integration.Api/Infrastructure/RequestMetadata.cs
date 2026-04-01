using Microsoft.Extensions.Primitives;

namespace Kalshi.Integration.Api.Infrastructure;

/// <summary>
/// Provides helpers for reading, normalizing, and echoing correlation and idempotency
/// headers on API requests and responses.
/// </summary>
public static class RequestMetadata
{
    /// <summary>
    /// Gets the header name used to carry request correlation identifiers.
    /// </summary>
    public const string CorrelationIdHeaderName = "x-correlation-id";

    /// <summary>
    /// Gets the header name used to carry idempotency keys.
    /// </summary>
    public const string IdempotencyKeyHeaderName = "idempotency-key";

    /// <summary>
    /// Gets the header name written when a response was served from an idempotent replay.
    /// </summary>
    public const string IdempotentReplayHeaderName = "x-idempotent-replay";

    /// <summary>
    /// Resolves the correlation identifier for the current request and echoes it on the response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="requestCorrelationId">An optional caller-supplied correlation identifier.</param>
    /// <returns>The resolved correlation identifier.</returns>
    public static string ResolveCorrelationId(HttpContext httpContext, string? requestCorrelationId = null)
    {
        var correlationId = !string.IsNullOrWhiteSpace(requestCorrelationId)
            ? requestCorrelationId.Trim()
            : TryReadHeader(httpContext, CorrelationIdHeaderName) ?? httpContext.TraceIdentifier;

        httpContext.Response.Headers[CorrelationIdHeaderName] = correlationId;
        return correlationId;
    }

    /// <summary>
    /// Resolves the idempotency key for the current request and echoes it on the response when present.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="fallback">An optional fallback idempotency key.</param>
    /// <returns>The resolved idempotency key, or <see langword="null"/> when none is available.</returns>
    public static string? ResolveIdempotencyKey(HttpContext httpContext, string? fallback = null)
    {
        var key = TryReadHeader(httpContext, IdempotencyKeyHeaderName);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            httpContext.Response.Headers[IdempotencyKeyHeaderName] = key;
        }

        return key;
    }

    /// <summary>
    /// Marks the response as an idempotent replay.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public static void MarkReplay(HttpContext httpContext)
    {
        httpContext.Response.Headers[IdempotentReplayHeaderName] = "true";
    }

    private static string? TryReadHeader(HttpContext httpContext, string headerName)
    {
        return httpContext.Request.Headers.TryGetValue(headerName, out StringValues values) && !StringValues.IsNullOrEmpty(values)
            ? values.ToString().Trim()
            : null;
    }
}
