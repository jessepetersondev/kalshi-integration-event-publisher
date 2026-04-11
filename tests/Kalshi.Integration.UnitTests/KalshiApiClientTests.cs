using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.UnitTests;

public sealed class KalshiApiClientTests
{
    [Fact]
    public async Task PlaceOrderAsync_ShouldSerializePriceFieldsAsStrings()
    {
        using RSA rsa = RSA.Create(2048);
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"order":{"order_id":"kalshi-order-1"}}"""),
        });

        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.elections.kalshi.com/trade-api/v2/"),
        };

        KalshiApiClient client = new(
            httpClient,
            Options.Create(new KalshiApiOptions
            {
                ApiKeyId = "test-key",
                PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
                Subaccount = 7,
                UserAgent = "kalshi-integration-event-publisher-tests",
            }));

        await client.PlaceOrderAsync(new JsonObject
        {
            ["ticker"] = "KXBTCD-TEST",
            ["client_order_id"] = "client-1",
            ["side"] = "no",
            ["action"] = "buy",
            ["count"] = 1,
            ["type"] = "limit",
            ["time_in_force"] = "good_til_cancelled",
            ["post_only"] = true,
            ["cancel_order_on_pause"] = false,
            ["subaccount"] = 7,
            ["no_price_dollars"] = 0.61m,
        });

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);

        JsonObject json = Assert.IsType<JsonObject>(JsonNode.Parse(handler.LastRequestBody));

        Assert.Equal("0.6100", json["no_price_dollars"]!.GetValue<string>());
        Assert.Equal("test-key", handler.LastRequest.Headers.GetValues("KALSHI-ACCESS-KEY").Single());
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory = responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
