using System.Net;
using Kalshi.Integration.Infrastructure.Integrations.NodeGateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.UnitTests;

public sealed class NodeGatewayClientTests
{
    [Fact]
    public async Task CorrelationPropagationHandler_ShouldCopyCorrelationIdFromIncomingRequest()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers["x-correlation-id"] = "corr-123";
        HttpContextAccessor httpContextAccessor = new() { HttpContext = httpContext };
        CapturingHandler innerHandler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}")
        });

        CorrelationPropagationHandler handler = new(httpContextAccessor)
        {
            InnerHandler = innerHandler,
        };

        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("http://localhost:3001"),
        };

        NodeGatewayClient client = new(
            httpClient,
            Options.Create(new NodeGatewayOptions { BaseUrl = "http://localhost:3001", HealthPath = "/health", TimeoutSeconds = 5, RetryAttempts = 0 }),
            NullLogger<NodeGatewayClient>.Instance);

        NodeGatewayProbeResult result = await client.ProbeHealthAsync();

        Assert.True(result.Healthy);
        Assert.Equal("corr-123", result.CorrelationId);
        Assert.Equal("corr-123", innerHandler.LastRequest!.Headers.GetValues("x-correlation-id").Single());
    }

    [Fact]
    public async Task NodeGatewayClient_ShouldReturnUnhealthyResultForNonSuccessStatus()
    {
        CapturingHandler innerHandler = new(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway down")
        });

        using HttpClient httpClient = new(innerHandler)
        {
            BaseAddress = new Uri("http://localhost:3001"),
        };

        NodeGatewayClient client = new(
            httpClient,
            Options.Create(new NodeGatewayOptions { BaseUrl = "http://localhost:3001", HealthPath = "/health", TimeoutSeconds = 5, RetryAttempts = 0 }),
            NullLogger<NodeGatewayClient>.Instance);

        NodeGatewayProbeResult result = await client.ProbeHealthAsync();

        Assert.False(result.Healthy);
        Assert.Equal(502, result.StatusCode);
        Assert.Equal("gateway down", result.ResponseBody);
    }

    [Fact]
    public async Task CorrelationPropagationHandler_ShouldFallBackToCurrentActivityTraceId()
    {
        HttpContextAccessor httpContextAccessor = new();
        CapturingHandler innerHandler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}")
        });

        CorrelationPropagationHandler handler = new(httpContextAccessor)
        {
            InnerHandler = innerHandler,
        };

        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("http://localhost:3001"),
        };

        using System.Diagnostics.Activity activity = new("test-request");
        activity.Start();

        NodeGatewayClient client = new(
            httpClient,
            Options.Create(new NodeGatewayOptions { BaseUrl = "http://localhost:3001", HealthPath = "/health", TimeoutSeconds = 5, RetryAttempts = 0 }),
            NullLogger<NodeGatewayClient>.Instance);

        await client.ProbeHealthAsync();

        Assert.Equal(activity.TraceId.ToString(), innerHandler.LastRequest!.Headers.GetValues("x-correlation-id").Single());
    }

    [Fact]
    public async Task NodeGatewayClient_ShouldThrowWhenDependencyCallFails()
    {
        ThrowingHandler innerHandler = new(new HttpRequestException("node gateway unavailable"));

        using HttpClient httpClient = new(innerHandler)
        {
            BaseAddress = new Uri("http://localhost:3001"),
        };

        NodeGatewayClient client = new(
            httpClient,
            Options.Create(new NodeGatewayOptions { BaseUrl = "http://localhost:3001", HealthPath = "/health", TimeoutSeconds = 5, RetryAttempts = 0 }),
            NullLogger<NodeGatewayClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ProbeHealthAsync());
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory = responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        private readonly Exception _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
}
