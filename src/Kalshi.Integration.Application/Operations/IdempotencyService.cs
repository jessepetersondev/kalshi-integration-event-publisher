using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;

namespace Kalshi.Integration.Application.Operations;

/// <summary>
/// Computes deterministic request hashes and stores replayable responses for endpoints
/// that support idempotent writes.
/// </summary>
public sealed class IdempotencyService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyStore _store;

    public IdempotencyService(IIdempotencyStore store)
    {
        _store = store;
    }

    public async Task<IdempotencyLookupResult> LookupAsync(string scope, string? key, object request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return IdempotencyLookupResult.None;
        }

        var normalizedKey = key.Trim();
        var requestHash = ComputeRequestHash(request);
        var existing = await _store.GetAsync(scope, normalizedKey, cancellationToken);
        if (existing is null)
        {
            return IdempotencyLookupResult.None;
        }

        return string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
            ? IdempotencyLookupResult.Replay(existing)
            : IdempotencyLookupResult.Conflict(existing);
    }

    public async Task SaveResponseAsync(string scope, string? key, object request, int statusCode, object response, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalizedKey = key.Trim();
        var requestHash = ComputeRequestHash(request);
        var responseBody = JsonSerializer.Serialize(response, SerializerOptions);

        await _store.SaveAsync(
            IdempotencyRecord.Create(scope, normalizedKey, requestHash, statusCode, responseBody),
            cancellationToken);
    }

    private static string ComputeRequestHash(object request)
    {
        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
/// <summary>
/// Defines the supported idempotency lookup status values.
/// </summary>


public enum IdempotencyLookupStatus
{
    None,
    Replay,
    Conflict,
}
/// <summary>
/// Represents the result of idempotency lookup.
/// </summary>


public sealed record IdempotencyLookupResult(IdempotencyLookupStatus Status, IdempotencyRecord? Record)
{
    public static IdempotencyLookupResult None { get; } = new(IdempotencyLookupStatus.None, null);

    public static IdempotencyLookupResult Replay(IdempotencyRecord record) => new(IdempotencyLookupStatus.Replay, record);

    public static IdempotencyLookupResult Conflict(IdempotencyRecord record) => new(IdempotencyLookupStatus.Conflict, record);
}
