using System.Text.Json;
using Kalshi.Integration.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalshi.Integration.UnitTests;

public sealed class HealthCheckResponseWriterTests
{
    [Fact]
    public async Task WriteJsonAsync_ShouldSerializeHealthReportPayload()
    {
        HealthReportEntry entry = new(
            status: HealthStatus.Unhealthy,
            description: "Database unavailable",
            duration: TimeSpan.FromMilliseconds(18),
            exception: new InvalidOperationException("boom"),
            data: new Dictionary<string, object>());
        HealthReport report = new(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = entry
            },
            TimeSpan.FromMilliseconds(25));

        DefaultHttpContext httpContext = new();
        await using MemoryStream responseStream = new();
        httpContext.Response.Body = responseStream;

        await HealthCheckResponseWriter.WriteJsonAsync(httpContext, report);

        responseStream.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(responseStream);

        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(25d, json.RootElement.GetProperty("totalDurationMs").GetDouble());
        JsonElement databaseEntry = json.RootElement.GetProperty("entries").GetProperty("database");
        Assert.Equal("Unhealthy", databaseEntry.GetProperty("status").GetString());
        Assert.Equal("Database unavailable", databaseEntry.GetProperty("description").GetString());
        Assert.Equal("boom", databaseEntry.GetProperty("error").GetString());
    }
}
