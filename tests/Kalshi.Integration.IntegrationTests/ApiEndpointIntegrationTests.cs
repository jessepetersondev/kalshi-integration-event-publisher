using System.Net;
using System.Net.Http.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.Positions;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Infrastructure.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kalshi.Integration.IntegrationTests;

public sealed class ApiEndpointIntegrationTests : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _anonymousClient;
    private readonly InMemoryApplicationEventPublisher _applicationEventPublisher;

    public ApiEndpointIntegrationTests(IntegrationTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient("admin", "trader", "operator", "integration");
        _anonymousClient = factory.CreateClient();
        _applicationEventPublisher = factory.Services.GetRequiredService<InMemoryApplicationEventPublisher>();
        _applicationEventPublisher.Reset();
    }

    [Fact]
    public async Task ProtectedEndpoints_ShouldRequireAuthentication()
    {
        HttpResponseMessage dashboardResponse = await _anonymousClient.GetAsync("/api/v1/dashboard/orders");
        HttpResponseMessage nodeGatewayResponse = await _anonymousClient.GetAsync("/api/v1/system/dependencies/node-gateway");

        Assert.Equal(HttpStatusCode.Unauthorized, dashboardResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nodeGatewayResponse.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_ShouldRejectUsersWithoutRequiredRole()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient("integration");
        HttpResponseMessage response = await client.GetAsync("/api/v1/dashboard/orders");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_ShouldRemainAvailableWithoutAuthentication()
    {
        HttpResponseMessage pingResponse = await _anonymousClient.GetAsync("/api/v1/system/ping");
        HttpResponseMessage liveResponse = await _anonymousClient.GetAsync("/health/live");
        HttpResponseMessage readyResponse = await _anonymousClient.GetAsync("/health/ready");

        pingResponse.EnsureSuccessStatusCode();
        liveResponse.EnsureSuccessStatusCode();
        readyResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Swagger_ShouldBeDisabledOutsideDevelopmentByDefault()
    {
        HttpResponseMessage response = await _anonymousClient.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentTokenEndpoint_ShouldIssueJwtForRequestedRoles()
    {
        HttpResponseMessage response = await _anonymousClient.PostAsJsonAsync("/api/v1/auth/dev-token", new { roles = new[] { "operator" }, subject = "local-docs-user" });
        response.EnsureSuccessStatusCode();

        DevTokenEnvelope? payload = await response.Content.ReadFromJsonAsync<DevTokenEnvelope>();
        Assert.NotNull(payload);
        Assert.Contains("operator", payload!.Roles);
        Assert.False(string.IsNullOrWhiteSpace(payload.AccessToken));
    }

    [Fact]
    public async Task PostTradeIntent_ShouldCreateTradeIntent()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-TEST", "yes", 2, 0.45m, "Breakout", null));
        response.EnsureSuccessStatusCode();

        TradeIntentResponse? payload = await response.Content.ReadFromJsonAsync<TradeIntentResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("KXBTC-TEST", payload!.Ticker);
    }

    [Fact]
    public async Task PostTradeIntent_ShouldReturnBadRequestForInvalidInput()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("", "yes", 0, 0m, "", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTradeIntent_ShouldRejectOversizedOrders()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-BIG", "yes", 99, 0.45m, "Breakout", "oversized-1"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTradeIntent_ShouldRejectDuplicateCorrelationIds()
    {
        string correlationId = $"dup-{Guid.NewGuid():N}";

        HttpResponseMessage first = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-DUP", "yes", 1, 0.45m, "Breakout", correlationId));
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-DUP", "no", 1, 0.55m, "Fade", correlationId));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task PostOrder_ShouldRejectDuplicateOrderForSameTradeIntent()
    {
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync(
            "/api/v1/trade-intents",
            new CreateTradeIntentRequest("KXBTC-DUP-ORDER", "yes", 1, 0.45m, "Breakout", $"dup-order-{Guid.NewGuid():N}"));
        tradeIntentResponse.EnsureSuccessStatusCode();

        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();

        HttpResponseMessage first = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent!.Id),
            ("idempotency-key", $"order-key-{Guid.NewGuid():N}"));
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent.Id),
            ("idempotency-key", $"order-key-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PostTradeIntent_ShouldReplayDuplicateIdempotencyKey()
    {
        string ticker = $"KXBTC-IDEMP-{Guid.NewGuid():N}".ToUpperInvariant();
        string idempotencyKey = $"trade-intent-{Guid.NewGuid():N}";
        string firstCorrelationId = $"corr-{Guid.NewGuid():N}";
        string secondCorrelationId = $"corr-{Guid.NewGuid():N}";
        CreateTradeIntentRequest payload = new(ticker, "yes", 2, 0.45m, "Idempotent", null);

        HttpResponseMessage first = await PostJsonWithHeadersAsync(
            "/api/v1/trade-intents",
            payload,
            ("idempotency-key", idempotencyKey),
            ("x-correlation-id", firstCorrelationId));

        HttpResponseMessage second = await PostJsonWithHeadersAsync(
            "/api/v1/trade-intents",
            payload,
            ("idempotency-key", idempotencyKey),
            ("x-correlation-id", secondCorrelationId));

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        TradeIntentResponse? firstBody = await first.Content.ReadFromJsonAsync<TradeIntentResponse>();
        TradeIntentResponse? secondBody = await second.Content.ReadFromJsonAsync<TradeIntentResponse>();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal(firstCorrelationId, GetHeaderValue(first, "x-correlation-id"));
        Assert.Equal(secondCorrelationId, GetHeaderValue(second, "x-correlation-id"));
        Assert.Equal("true", GetHeaderValue(second, "x-idempotent-replay"));
    }

    [Fact]
    public async Task HealthEndpoints_ShouldExposeLivenessAndReadinessChecks()
    {
        HttpResponseMessage liveResponse = await _client.GetAsync("/health/live");
        HttpResponseMessage readyResponse = await _client.GetAsync("/health/ready");

        liveResponse.EnsureSuccessStatusCode();
        readyResponse.EnsureSuccessStatusCode();

        string liveBody = await liveResponse.Content.ReadAsStringAsync();
        string readyBody = await readyResponse.Content.ReadAsStringAsync();

        Assert.Contains("\"status\": \"Healthy\"", liveBody, StringComparison.Ordinal);
        Assert.Contains("\"self\"", liveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"database\"", liveBody, StringComparison.Ordinal);

        Assert.Contains("\"status\": \"Healthy\"", readyBody, StringComparison.Ordinal);
        Assert.Contains("\"self\"", readyBody, StringComparison.Ordinal);
        Assert.Contains("\"database\"", readyBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RiskValidate_ShouldReturnExplicitDecisionOutput()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/risk/validate", new CreateTradeIntentRequest("KXBTC-RISK", "yes", 2, 0.44m, "Check", "risk-1"));
        response.EnsureSuccessStatusCode();

        RiskDecisionResponse? payload = await response.Content.ReadFromJsonAsync<RiskDecisionResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Accepted);
        Assert.Equal("accepted", payload.Decision);
    }

    [Fact]
    public async Task OrderFlow_ShouldCreateOrderAndReturnItById()
    {
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-TEST2", "no", 1, 0.55m, "Fade", null));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();

        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        createOrderResponse.EnsureSuccessStatusCode();

        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal(HttpStatusCode.Created, createOrderResponse.StatusCode);

        HttpResponseMessage lookup = await _client.GetAsync($"/api/v1/orders/{order!.Id}");
        lookup.EnsureSuccessStatusCode();

        OrderResponse? fetched = await lookup.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(order.Id, fetched!.Id);
        Assert.Single(fetched.Events);
    }

    [Fact]
    public async Task OrderOutcomesEndpoint_ShouldReturnPublisherOwnedExecutionOutcomeState()
    {
        string correlationId = $"outcome-{Guid.NewGuid():N}";
        string ticker = $"KXBTC-OUTCOME-{Guid.NewGuid():N}".ToUpperInvariant();

        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync(
            "/api/v1/trade-intents",
            new CreateTradeIntentRequest(
                ticker,
                "yes",
                2,
                0.46m,
                "Outcome",
                correlationId,
                OriginService: "weather-quant"));
        tradeIntentResponse.EnsureSuccessStatusCode();

        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        createOrderResponse.EnsureSuccessStatusCode();
        OrderResponse? createdOrder = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            TradingService tradingService = scope.ServiceProvider.GetRequiredService<TradingService>();
            ApplicationEventEnvelope resultEvent = ApplicationEventEnvelope.Create(
                category: "integration",
                name: "order.execution_succeeded",
                resourceId: createdOrder!.Id.ToString(),
                correlationId: correlationId,
                attributes: new Dictionary<string, string?>
                {
                    ["publisherOrderId"] = createdOrder.Id.ToString(),
                    ["orderStatus"] = "accepted",
                    ["externalOrderId"] = "ext-outcome-1",
                    ["clientOrderId"] = "client-outcome-1",
                },
                occurredAt: DateTimeOffset.UtcNow.AddSeconds(1));

            bool applied = await tradingService.ApplyExecutorResultAsync(resultEvent);
            Assert.True(applied);
        }

        List<OrderOutcomeResponse>? outcomes = await _client.GetFromJsonAsync<List<OrderOutcomeResponse>>(
            $"/api/v1/orders/outcomes?correlationId={Uri.EscapeDataString(correlationId)}&originService=weather-quant&outcomeState=succeeded&limit=10");

        Assert.NotNull(outcomes);
        OrderOutcomeResponse outcome = Assert.Single(outcomes!);
        Assert.Equal(createdOrder!.Id, outcome.Id);
        Assert.Equal("weather-quant", outcome.OriginService);
        Assert.Equal("succeeded", outcome.OutcomeState);
        Assert.Equal("order.execution_succeeded", outcome.LastResultStatus);
        Assert.Equal("ext-outcome-1", outcome.ExternalOrderId);
        Assert.Equal("publishconfirmed", outcome.PublishStatus);
    }

    [Fact]
    public async Task PostOrder_ShouldReplayDuplicateIdempotencyKeyWithoutCreatingSecondOrder()
    {
        string ticker = $"KXBTC-ORDER-{Guid.NewGuid():N}".ToUpperInvariant();
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest(ticker, "no", 1, 0.58m, "OrderReplay", $"order-intent-{Guid.NewGuid():N}"));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        string idempotencyKey = $"order-{Guid.NewGuid():N}";

        HttpResponseMessage first = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent!.Id),
            ("idempotency-key", idempotencyKey),
            ("x-correlation-id", $"corr-{Guid.NewGuid():N}"));

        HttpResponseMessage second = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent.Id),
            ("idempotency-key", idempotencyKey),
            ("x-correlation-id", $"corr-{Guid.NewGuid():N}"));

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        OrderResponse? firstBody = await first.Content.ReadFromJsonAsync<OrderResponse>();
        OrderResponse? secondBody = await second.Content.ReadFromJsonAsync<OrderResponse>();
        List<DashboardOrderSummaryResponse>? orders = await _client.GetFromJsonAsync<List<DashboardOrderSummaryResponse>>("/api/v1/dashboard/orders");

        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);
        Assert.NotNull(orders);
        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal("true", GetHeaderValue(second, "x-idempotent-replay"));
        Assert.Single(orders!.Where(order => order.Ticker == ticker));
    }

    [Fact]
    public async Task ApplicationEvents_ShouldPublishForSuccessfulTradeIntentOrderAndExecutionUpdateFlows()
    {
        string ticker = $"KXBTC-PUB-{Guid.NewGuid():N}".ToUpperInvariant();
        string tradeCorrelationId = $"trade-pub-{Guid.NewGuid():N}";
        string orderCorrelationId = $"order-pub-{Guid.NewGuid():N}";
        string executionCorrelationId = $"exec-pub-{Guid.NewGuid():N}";

        HttpResponseMessage tradeIntentResponse = await PostJsonWithHeadersAsync(
            "/api/v1/trade-intents",
            new CreateTradeIntentRequest(ticker, "yes", 2, 0.48m, "Publisher", null),
            ("x-correlation-id", tradeCorrelationId),
            ("idempotency-key", $"trade-key-{Guid.NewGuid():N}"));
        tradeIntentResponse.EnsureSuccessStatusCode();
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();

        HttpResponseMessage orderResponse = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent!.Id),
            ("x-correlation-id", orderCorrelationId),
            ("idempotency-key", $"order-key-{Guid.NewGuid():N}"));
        orderResponse.EnsureSuccessStatusCode();
        OrderResponse? order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        HttpResponseMessage executionResponse = await PostJsonWithHeadersAsync(
            "/api/v1/integrations/execution-updates",
            new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, executionCorrelationId));
        executionResponse.EnsureSuccessStatusCode();

        IReadOnlyList<ApplicationEventEnvelope> publishedEvents = _applicationEventPublisher.GetPublishedEvents();
        Assert.Equal(3, publishedEvents.Count);

        Assert.Contains(publishedEvents, applicationEvent =>
            applicationEvent.Name == "trade-intent.created"
            && applicationEvent.CorrelationId == tradeCorrelationId
            && applicationEvent.ResourceId == tradeIntent.Id.ToString()
            && applicationEvent.Attributes.TryGetValue("ticker", out string? tickerValue)
            && tickerValue == ticker);

        Assert.Contains(publishedEvents, applicationEvent =>
            applicationEvent.Name == "order.created"
            && applicationEvent.CorrelationId == orderCorrelationId
            && applicationEvent.ResourceId == order.Id.ToString());

        Assert.Contains(publishedEvents, applicationEvent =>
            applicationEvent.Name == "execution-update.applied"
            && applicationEvent.CorrelationId == executionCorrelationId
            && applicationEvent.ResourceId == order.Id.ToString());
    }

    [Fact]
    public async Task GetOrder_ShouldReturnNotFoundForUnknownOrder()
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExecutionUpdate_ShouldApplyStateTransitionAndAppendHistory()
    {
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-EXEC", "yes", 3, 0.47m, "Exec", null));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, "corr-a"));
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order.Id, "partially_filled", 2, DateTimeOffset.UtcNow.AddSeconds(1), "corr-b"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        OrderResponse? lookup = await _client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}");
        Assert.NotNull(lookup);
        Assert.Equal("partiallyfilled", lookup!.Status);
        Assert.Equal(2, lookup.FilledQuantity);
        Assert.True(lookup.Events.Count >= 3);
    }

    [Fact]
    public async Task ExecutionUpdate_ShouldReplayRetriedInboundEventSafely()
    {
        string ticker = $"KXBTC-RETRY-{Guid.NewGuid():N}".ToUpperInvariant();
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest(ticker, "yes", 3, 0.47m, "Retry", $"retry-intent-{Guid.NewGuid():N}"));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, $"accept-{Guid.NewGuid():N}"));

        string correlationId = $"event-{Guid.NewGuid():N}";
        ExecutionUpdateRequest request = new(order.Id, "partially_filled", 2, DateTimeOffset.UtcNow.AddSeconds(1), correlationId);

        HttpResponseMessage first = await PostJsonWithHeadersAsync("/api/v1/integrations/execution-updates", request);
        HttpResponseMessage second = await PostJsonWithHeadersAsync("/api/v1/integrations/execution-updates", request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        OrderResponse? lookup = await _client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}");
        IReadOnlyList<ApplicationEventEnvelope> publishedEvents = _applicationEventPublisher.GetPublishedEvents();

        Assert.NotNull(lookup);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal("true", GetHeaderValue(second, "x-idempotent-replay"));
        Assert.Equal("partiallyfilled", lookup!.Status);
        Assert.Equal(2, lookup.FilledQuantity);
        Assert.Equal(3, lookup.Events.Count);
        Assert.Equal(4, publishedEvents.Count);
        Assert.Single(publishedEvents.Where(applicationEvent =>
            applicationEvent.Name == "execution-update.applied"
            && applicationEvent.CorrelationId == correlationId));
    }

    [Fact]
    public async Task ExecutionUpdate_ShouldReturnBadRequestForIllegalTransition()
    {
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-ILLEGAL", "yes", 1, 0.44m, "Exec", null));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "settled", 0, DateTimeOffset.UtcNow, "corr-c"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPositions_ShouldReturnUpdatedSnapshotsAfterExecution()
    {
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-POS", "yes", 4, 0.60m, "Trend", null));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, "corr-d"));
        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order.Id, "partially_filled", 2, DateTimeOffset.UtcNow.AddSeconds(1), "corr-e"));

        HttpResponseMessage response = await _client.GetAsync("/api/v1/positions");
        response.EnsureSuccessStatusCode();

        List<PositionResponse>? positions = await response.Content.ReadFromJsonAsync<List<PositionResponse>>();
        Assert.NotNull(positions);
        PositionResponse position = Assert.Single(positions!.Where(position => position.Ticker == "KXBTC-POS"));
        Assert.Equal(2, position.Contracts);
    }

    [Fact]
    public async Task Dashboard_ShouldServeStaticShell()
    {
        HttpResponseMessage response = await _client.GetAsync("/dashboard");
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Operator Dashboard", html);
        Assert.Contains("Execution Outcomes", html);
        Assert.Contains("Audit Trail", html);
        Assert.Contains("Live data only", html);
        Assert.Contains("Validation & Integration Issues", html);
    }

    [Fact]
    public async Task DashboardEndpoints_ShouldReturnOrdersPositionsAndEvents()
    {
        string ticker = $"KXBTC-DASH-{Guid.NewGuid():N}".ToUpperInvariant();
        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest(ticker, "yes", 5, 0.51m, "Dashboard", $"dash-{Guid.NewGuid():N}"));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, $"exec-{Guid.NewGuid():N}"));
        await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order.Id, "partially_filled", 3, DateTimeOffset.UtcNow.AddSeconds(1), $"exec-{Guid.NewGuid():N}"));

        List<DashboardOrderSummaryResponse>? orders = await _client.GetFromJsonAsync<List<DashboardOrderSummaryResponse>>("/api/v1/dashboard/orders");
        List<PositionResponse>? positions = await _client.GetFromJsonAsync<List<PositionResponse>>("/api/v1/dashboard/positions");
        List<DashboardEventResponse>? events = await _client.GetFromJsonAsync<List<DashboardEventResponse>>("/api/v1/dashboard/events?limit=20");

        Assert.NotNull(orders);
        Assert.NotNull(positions);
        Assert.NotNull(events);

        DashboardOrderSummaryResponse dashboardOrder = Assert.Single(orders!.Where(item => item.Ticker == ticker));
        Assert.Equal("partiallyfilled", dashboardOrder.Status);
        Assert.Equal(3, dashboardOrder.FilledQuantity);

        PositionResponse position = Assert.Single(positions!.Where(item => item.Ticker == ticker));
        Assert.Equal(3, position.Contracts);

        Assert.Contains(events!, item => item.OrderId == order.Id && item.Status == "partiallyfilled" && item.FilledQuantity == 3);
    }

    [Fact]
    public async Task DashboardAuditRecords_ShouldExposeCorrelationAndIdempotencyMetadata()
    {
        string ticker = $"KXBTC-AUDIT-{Guid.NewGuid():N}".ToUpperInvariant();
        string tradeIntentCorrelationId = $"trade-corr-{Guid.NewGuid():N}";
        string tradeIntentIdempotencyKey = $"trade-key-{Guid.NewGuid():N}";
        string orderCorrelationId = $"order-corr-{Guid.NewGuid():N}";
        string orderIdempotencyKey = $"order-key-{Guid.NewGuid():N}";
        string executionCorrelationId = $"exec-corr-{Guid.NewGuid():N}";

        HttpResponseMessage tradeIntentResponse = await PostJsonWithHeadersAsync(
            "/api/v1/trade-intents",
            new CreateTradeIntentRequest(ticker, "yes", 2, 0.49m, "Audit", null),
            ("idempotency-key", tradeIntentIdempotencyKey),
            ("x-correlation-id", tradeIntentCorrelationId));
        tradeIntentResponse.EnsureSuccessStatusCode();

        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();

        HttpResponseMessage orderResponse = await PostJsonWithHeadersAsync(
            "/api/v1/orders",
            new CreateOrderRequest(tradeIntent!.Id),
            ("idempotency-key", orderIdempotencyKey),
            ("x-correlation-id", orderCorrelationId));
        orderResponse.EnsureSuccessStatusCode();

        OrderResponse? order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        HttpResponseMessage executionUpdateResponse = await PostJsonWithHeadersAsync(
            "/api/v1/integrations/execution-updates",
            new ExecutionUpdateRequest(order!.Id, "accepted", 0, DateTimeOffset.UtcNow, executionCorrelationId));
        executionUpdateResponse.EnsureSuccessStatusCode();

        List<DashboardAuditRecordResponse>? auditRecords = await _client.GetFromJsonAsync<List<DashboardAuditRecordResponse>>("/api/v1/dashboard/audit-records?hours=168&limit=200");
        Assert.NotNull(auditRecords);

        Assert.Contains(auditRecords!, record =>
            record.Action == "trade_intent.created"
            && record.CorrelationId == tradeIntentCorrelationId
            && record.IdempotencyKey == tradeIntentIdempotencyKey
            && record.ResourceId == tradeIntent.Id.ToString());

        Assert.Contains(auditRecords!, record =>
            record.Action == "order.created"
            && record.CorrelationId == orderCorrelationId
            && record.IdempotencyKey == orderIdempotencyKey
            && record.ResourceId == order.Id.ToString());

        Assert.Contains(auditRecords!, record =>
            record.Action == "execution_update.applied"
            && record.CorrelationId == executionCorrelationId
            && record.ResourceId == order.Id.ToString());
    }

    [Fact]
    public async Task DashboardIssues_ShouldExposeValidationAndIntegrationFailures()
    {
        string validationCorrelationId = $"validation-{Guid.NewGuid():N}";
        HttpResponseMessage validationResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-INVALID", "yes", 99, 0.45m, "Validation", validationCorrelationId));
        Assert.Equal(HttpStatusCode.BadRequest, validationResponse.StatusCode);

        HttpResponseMessage tradeIntentResponse = await _client.PostAsJsonAsync("/api/v1/trade-intents", new CreateTradeIntentRequest("KXBTC-ISSUES", "yes", 1, 0.52m, "Issues", $"issue-{Guid.NewGuid():N}"));
        TradeIntentResponse? tradeIntent = await tradeIntentResponse.Content.ReadFromJsonAsync<TradeIntentResponse>();
        HttpResponseMessage createOrderResponse = await _client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest(tradeIntent!.Id));
        OrderResponse? order = await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        HttpResponseMessage integrationResponse = await _client.PostAsJsonAsync("/api/v1/integrations/execution-updates", new ExecutionUpdateRequest(order!.Id, "settled", 0, DateTimeOffset.UtcNow, $"bad-exec-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.BadRequest, integrationResponse.StatusCode);

        List<DashboardIssueResponse>? validationIssues = await _client.GetFromJsonAsync<List<DashboardIssueResponse>>("/api/v1/dashboard/issues?category=validation&hours=168");
        List<DashboardIssueResponse>? integrationIssues = await _client.GetFromJsonAsync<List<DashboardIssueResponse>>("/api/v1/dashboard/issues?category=integration&hours=168");

        Assert.NotNull(validationIssues);
        Assert.NotNull(integrationIssues);
        Assert.Contains(validationIssues!, issue => issue.Details is not null && issue.Details.Contains(validationCorrelationId, StringComparison.Ordinal) && issue.Severity == "warning");
        Assert.Contains(integrationIssues!, issue => issue.Details is not null && issue.Details.Contains(order.Id.ToString(), StringComparison.Ordinal) && issue.Severity == "error");
    }

    private async Task<HttpResponseMessage> PostJsonWithHeadersAsync<T>(string url, T payload, params (string Name, string Value)[] headers)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        foreach ((string name, string value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await _client.SendAsync(request);
    }

    private static string GetHeaderValue(HttpResponseMessage response, string headerName)
    {
        Assert.True(response.Headers.TryGetValues(headerName, out IEnumerable<string>? values), $"Expected header '{headerName}' to be present.");
        return Assert.Single(values);
    }
}

file sealed record DevTokenEnvelope(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, IReadOnlyList<string> Roles, string Issuer, string Audience);
