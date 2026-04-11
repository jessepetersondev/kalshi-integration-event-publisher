using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Infrastructure.Operations;

namespace Kalshi.Integration.UnitTests;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task LookupAsync_ShouldReturnNoneWhenKeyMissing()
    {
        InMemoryIdempotencyStore store = new();
        IdempotencyService service = new(store);

        IdempotencyLookupResult result = await service.LookupAsync("trade-intents", null, new { ticker = "KXBTC" });

        Assert.Equal(IdempotencyLookupStatus.None, result.Status);
        Assert.Null(result.Record);
    }

    [Fact]
    public async Task SaveResponseAsync_ShouldIgnoreBlankKeys()
    {
        InMemoryIdempotencyStore store = new();
        IdempotencyService service = new(store);

        await service.SaveResponseAsync("trade-intents", " ", new { ticker = "KXBTC" }, 201, new { id = Guid.NewGuid() });
        IdempotencyRecord? lookup = await store.GetAsync("trade-intents", "", CancellationToken.None);

        Assert.Null(lookup);
    }

    [Fact]
    public async Task LookupAsync_ShouldReplayWhenKeyAndPayloadMatchSavedResponse()
    {
        InMemoryIdempotencyStore store = new();
        IdempotencyService service = new(store);
        var request = new { ticker = "KXBTC", side = "yes", quantity = 1 };

        await service.SaveResponseAsync("trade-intents", " idem-1 ", request, 201, new { id = Guid.NewGuid(), ticker = "KXBTC" });
        IdempotencyLookupResult result = await service.LookupAsync("trade-intents", "idem-1", request);

        Assert.Equal(IdempotencyLookupStatus.Replay, result.Status);
        Assert.NotNull(result.Record);
        Assert.Equal(201, result.Record!.StatusCode);
        Assert.Equal("idem-1", result.Record.Key);
    }

    [Fact]
    public async Task LookupAsync_ShouldConflictWhenKeyMatchesButPayloadDiffers()
    {
        InMemoryIdempotencyStore store = new();
        IdempotencyService service = new(store);

        await service.SaveResponseAsync("orders", "idem-2", new { tradeIntentId = Guid.NewGuid() }, 201, new { id = Guid.NewGuid() });
        IdempotencyLookupResult result = await service.LookupAsync("orders", "idem-2", new { tradeIntentId = Guid.NewGuid() });

        Assert.Equal(IdempotencyLookupStatus.Conflict, result.Status);
        Assert.NotNull(result.Record);
    }
}
