using Kalshi.Integration.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Kalshi.Integration.UnitTests;

public sealed class RequestMetadataTests
{
    [Fact]
    public void ResolveCorrelationId_ShouldPreferExplicitRequestCorrelationIdAndWriteResponseHeader()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers[RequestMetadata.CorrelationIdHeaderName] = new StringValues("header-correlation");

        string correlationId = RequestMetadata.ResolveCorrelationId(httpContext, " payload-correlation ");

        Assert.Equal("payload-correlation", correlationId);
        Assert.Equal("payload-correlation", httpContext.Response.Headers[RequestMetadata.CorrelationIdHeaderName].ToString());
    }

    [Fact]
    public void ResolveCorrelationId_ShouldFallBackToHeaderBeforeTraceIdentifier()
    {
        DefaultHttpContext httpContext = new()
        {
            TraceIdentifier = "trace-123"
        };
        httpContext.Request.Headers[RequestMetadata.CorrelationIdHeaderName] = new StringValues(" header-correlation ");

        string correlationId = RequestMetadata.ResolveCorrelationId(httpContext);

        Assert.Equal("header-correlation", correlationId);
        Assert.Equal("header-correlation", httpContext.Response.Headers[RequestMetadata.CorrelationIdHeaderName].ToString());
    }

    [Fact]
    public void ResolveCorrelationId_ShouldFallBackToTraceIdentifierWhenHeaderMissing()
    {
        DefaultHttpContext httpContext = new()
        {
            TraceIdentifier = "trace-456"
        };

        string correlationId = RequestMetadata.ResolveCorrelationId(httpContext);

        Assert.Equal("trace-456", correlationId);
        Assert.Equal("trace-456", httpContext.Response.Headers[RequestMetadata.CorrelationIdHeaderName].ToString());
    }

    [Fact]
    public void ResolveIdempotencyKey_ShouldFallBackToHeaderAndEchoResponseHeader()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers[RequestMetadata.IdempotencyKeyHeaderName] = new StringValues(" idem-header ");

        string? idempotencyKey = RequestMetadata.ResolveIdempotencyKey(httpContext);

        Assert.Equal("idem-header", idempotencyKey);
        Assert.Equal("idem-header", httpContext.Response.Headers[RequestMetadata.IdempotencyKeyHeaderName].ToString());
    }

    [Fact]
    public void ResolveIdempotencyKey_ShouldUseFallbackWhenHeaderMissing()
    {
        DefaultHttpContext httpContext = new();

        string? idempotencyKey = RequestMetadata.ResolveIdempotencyKey(httpContext, " fallback-key ");

        Assert.Equal("fallback-key", idempotencyKey);
        Assert.Equal("fallback-key", httpContext.Response.Headers[RequestMetadata.IdempotencyKeyHeaderName].ToString());
    }

    [Fact]
    public void ResolveIdempotencyKey_ShouldReturnNullWhenNoValueExists()
    {
        DefaultHttpContext httpContext = new();

        string? idempotencyKey = RequestMetadata.ResolveIdempotencyKey(httpContext);

        Assert.Null(idempotencyKey);
        Assert.False(httpContext.Response.Headers.ContainsKey(RequestMetadata.IdempotencyKeyHeaderName));
    }

    [Fact]
    public void MarkReplay_ShouldSetReplayHeader()
    {
        DefaultHttpContext httpContext = new();

        RequestMetadata.MarkReplay(httpContext);

        Assert.Equal("true", httpContext.Response.Headers[RequestMetadata.IdempotentReplayHeaderName].ToString());
    }
}
