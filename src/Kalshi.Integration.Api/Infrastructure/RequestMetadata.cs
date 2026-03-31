using Microsoft.Extensions.Primitives;

namespace Kalshi.Integration.Api.Infrastructure;

/// <summary>
/// Provides helpers for reading, normalizing, and echoing correlation and idempotency
/// headers on API requests and responses.
/// </summary>
public static class RequestMetadata
{
    public const string CorrelationIdHeaderName = "x-correlation-id";
    public const string IdempotencyKeyHeaderName = "idempotency-key";
    public const string IdempotentReplayHeaderName = "x-idempotent-replay";

    public static string ResolveCorrelationId(HttpContext httpContext, string? requestCorrelationId = null)
    {
        var correlationId = !string.IsNullOrWhiteSpace(requestCorrelationId)
            ? requestCorrelationId.Trim()
            : TryReadHeader(httpContext, CorrelationIdHeaderName) ?? httpContext.TraceIdentifier;

        httpContext.Response.Headers[CorrelationIdHeaderName] = correlationId;
        return correlationId;
    }

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
