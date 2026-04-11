using Kalshi.Integration.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Kalshi.Integration.UnitTests;

public sealed class RequestTimingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldLogStructuredRequestTimingForSuccessfulRequests()
    {
        TestLogger<RequestTimingMiddleware> logger = new();
        RequestTimingMiddleware middleware = new(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                await Task.CompletedTask;
            },
            logger);

        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/health/live";
        httpContext.Request.Headers[RequestMetadata.CorrelationIdHeaderName] = new StringValues("corr-success");

        await middleware.InvokeAsync(httpContext);

        TestLogEntry logEntry = Assert.Single(logger.Entries.Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information));
        Assert.Contains("Request completed GET /health/live", logEntry.Message, StringComparison.Ordinal);
        Assert.Contains("statusCode=204", logEntry.Message, StringComparison.Ordinal);
        Assert.Contains("correlationId=corr-success", logEntry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogErrorContextForFailedRequests()
    {
        TestLogger<RequestTimingMiddleware> logger = new();
        RequestTimingMiddleware middleware = new(
            _ => throw new InvalidOperationException("boom"),
            logger);

        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/v1/orders";
        httpContext.Request.Headers[RequestMetadata.CorrelationIdHeaderName] = new StringValues("corr-failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));

        TestLogEntry logEntry = Assert.Single(logger.Entries.Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error));
        Assert.Contains("Request failed POST /api/v1/orders", logEntry.Message, StringComparison.Ordinal);
        Assert.Contains("correlationId=corr-failure", logEntry.Message, StringComparison.Ordinal);
        Assert.NotNull(logEntry.Exception);
    }
}
