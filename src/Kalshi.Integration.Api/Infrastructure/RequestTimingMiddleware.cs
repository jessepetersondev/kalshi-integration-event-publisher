using System.Diagnostics;
using Kalshi.Integration.Contracts.Diagnostics;

namespace Kalshi.Integration.Api.Infrastructure;

/// <summary>
/// Applies request timing concerns to the ASP.NET Core request pipeline.
/// </summary>


public sealed class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = httpContext.Request;
        var correlationId = httpContext.Request.Headers.TryGetValue(RequestMetadata.CorrelationIdHeaderName, out var headerValue)
            ? headerValue.ToString()
            : httpContext.TraceIdentifier;

        try
        {
            await _next(httpContext);
            stopwatch.Stop();

            var path = request.Path.Value ?? "/";
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            KalshiTelemetry.HttpServerRequestDurationMs.Record(
                elapsedMs,
                new KeyValuePair<string, object?>("http.request.method", request.Method),
                new KeyValuePair<string, object?>("http.route", path),
                new KeyValuePair<string, object?>("http.response.status_code", httpContext.Response.StatusCode));

            _logger.LogInformation(
                "Request completed {Method} {Path} with statusCode={StatusCode} in {ElapsedMs} ms. correlationId={CorrelationId} traceId={TraceIdentifier}",
                request.Method,
                path,
                httpContext.Response.StatusCode,
                elapsedMs,
                correlationId,
                httpContext.TraceIdentifier);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            var path = request.Path.Value ?? "/";
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            KalshiTelemetry.HttpServerRequestDurationMs.Record(
                elapsedMs,
                new KeyValuePair<string, object?>("http.request.method", request.Method),
                new KeyValuePair<string, object?>("http.route", path),
                new KeyValuePair<string, object?>("error.type", exception.GetType().Name));

            _logger.LogError(
                exception,
                "Request failed {Method} {Path} after {ElapsedMs} ms. correlationId={CorrelationId} traceId={TraceIdentifier}",
                request.Method,
                path,
                elapsedMs,
                correlationId,
                httpContext.TraceIdentifier);

            throw;
        }
    }
}
