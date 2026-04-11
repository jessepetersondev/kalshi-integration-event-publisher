using System.Diagnostics;
using Kalshi.Integration.Contracts.Diagnostics;

namespace Kalshi.Integration.Api.Infrastructure;

/// <summary>
/// Applies request timing concerns to the ASP.NET Core request pipeline.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RequestTimingMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the request pipeline.</param>
/// <param name="logger">The logger used for request completion and failure events.</param>
public sealed class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestTimingMiddleware> _logger = logger;

    /// <summary>
    /// Measures the current request, records HTTP telemetry, and logs success or failure details.
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpRequest request = httpContext.Request;
        string correlationId = httpContext.Request.Headers.TryGetValue(RequestMetadata.CorrelationIdHeaderName, out Microsoft.Extensions.Primitives.StringValues headerValue)
            ? headerValue.ToString()
            : httpContext.TraceIdentifier;

        try
        {
            await _next(httpContext);
            stopwatch.Stop();

            string path = request.Path.Value ?? "/";
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

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

            string path = request.Path.Value ?? "/";
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

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
