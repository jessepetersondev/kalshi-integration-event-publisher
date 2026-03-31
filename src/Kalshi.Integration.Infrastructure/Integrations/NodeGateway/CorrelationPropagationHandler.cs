using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Handles correlation propagation events.
/// </summary>


public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    private const string CorrelationIdHeaderName = "x-correlation-id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationPropagationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(CorrelationIdHeaderName))
        {
            var correlationId = _httpContextAccessor.HttpContext?.Request.Headers[CorrelationIdHeaderName].ToString();

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            }

            request.Headers.TryAddWithoutValidation(CorrelationIdHeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
