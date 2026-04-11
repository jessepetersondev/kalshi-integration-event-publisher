using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalshi.Integration.Executor.Infrastructure;

/// <summary>
/// Writes health-check results as a compact JSON payload.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                static entry => entry.Key,
                static entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
