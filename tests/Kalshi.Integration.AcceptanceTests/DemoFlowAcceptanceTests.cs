using System.Net;
using System.Net.Http.Json;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.Positions;
using Kalshi.Integration.Contracts.TradeIntents;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kalshi.Integration.AcceptanceTests;

public sealed class DemoFlowAcceptanceTests(AcceptanceTestWebApplicationFactory factory) : IClassFixture<AcceptanceTestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient("admin", "trader", "operator", "integration");

    [Fact]
    public async Task HappyPath_ShouldMoveFromTradeIntentToVisibleDashboardState()
    {
        string ticker = $"KXBTC-DEMO-{Guid.NewGuid():N}".ToUpperInvariant();

        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync(
            "/api/v1/trade-intents",
            new CreateTradeIntentRequest(ticker, "yes", 2, 0.49m, "Demo", $"demo-intent-{Guid.NewGuid():N}"));
        tradeIntentResponse.EnsureSuccessStatusCode();
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();

        HttpResponseMessage orderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        orderResponse.EnsureSuccessStatusCode();
        OrderResponse? order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        await _client.PostAsJsonAsync(
            "/api/v1/integrations/execution-updates",
            new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, $"demo-accept-{Guid.NewGuid():N}"));

        HttpResponseMessage executionResponse = await _client.PostAsJsonAsync(
            "/api/v1/integrations/execution-updates",
            new ExecutionUpdateRequest(order.Id, "partially_filled", 1, DateTimeOffset.UtcNow.AddSeconds(1), $"demo-fill-{Guid.NewGuid():N}"));
        executionResponse.EnsureSuccessStatusCode();

        OrderResponse? fetchedOrder = await _client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}");
        List<DashboardOrderSummaryResponse>? dashboardOrders = await _client.GetFromJsonAsync<List<DashboardOrderSummaryResponse>>("/api/v1/dashboard/orders");
        List<PositionResponse>? dashboardPositions = await _client.GetFromJsonAsync<List<PositionResponse>>("/api/v1/dashboard/positions");
        List<DashboardEventResponse>? dashboardEvents = await _client.GetFromJsonAsync<List<DashboardEventResponse>>("/api/v1/dashboard/events?limit=20");

        Assert.NotNull(fetchedOrder);
        Assert.Equal("partiallyfilled", fetchedOrder!.Status);
        Assert.Equal(1, fetchedOrder.FilledQuantity);

        DashboardOrderSummaryResponse dashboardOrder = Assert.Single(dashboardOrders!.Where(item => item.Ticker == ticker));
        Assert.Equal("partiallyfilled", dashboardOrder.Status);
        Assert.Equal(1, dashboardOrder.FilledQuantity);

        PositionResponse dashboardPosition = Assert.Single(dashboardPositions!.Where(item => item.Ticker == ticker));
        Assert.Equal(1, dashboardPosition.Contracts);

        Assert.Contains(dashboardEvents!, item => item.OrderId == order.Id && item.Status == "partiallyfilled" && item.FilledQuantity == 1);
    }

    [Fact]
    public async Task DemoSurface_ShouldExposeHealthChecksAndDashboardShell()
    {
        HttpResponseMessage liveResponse = await _client.GetAsync("/health/live");
        HttpResponseMessage readyResponse = await _client.GetAsync("/health/ready");
        HttpResponseMessage dashboardResponse = await _client.GetAsync("/dashboard");

        liveResponse.EnsureSuccessStatusCode();
        readyResponse.EnsureSuccessStatusCode();
        dashboardResponse.EnsureSuccessStatusCode();

        string dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();

        Assert.Contains("Operator Dashboard", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Audit Trail", dashboardHtml, StringComparison.Ordinal);
    }
}
