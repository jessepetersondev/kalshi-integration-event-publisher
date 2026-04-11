using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;

namespace Kalshi.Integration.Application.Operations;

/// <summary>
/// Computes deterministic request hashes and stores replayable responses for endpoints
/// that support idempotent writes.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="IdempotencyService"/> class.
/// </remarks>
/// <param name="store">The store used to persist idempotency fingerprints and replay payloads.</param>
public sealed class IdempotencyService(IIdempotencyStore store)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyStore _store = store;

    public async Task<IdempotencyLookupResult> LookupAsync(string scope, string? key, object request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return IdempotencyLookupResult.None;
        }

        string normalizedKey = key.Trim();
        string requestHash = ComputeRequestHash(request);
        IdempotencyRecord? existing = await _store.GetAsync(scope, normalizedKey, cancellationToken);
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

        string normalizedKey = key.Trim();
        string requestHash = ComputeRequestHash(request);
        string responseBody = JsonSerializer.Serialize(response, SerializerOptions);

        await _store.SaveAsync(
            IdempotencyRecord.Create(scope, normalizedKey, requestHash, statusCode, responseBody),
            cancellationToken);
    }

    private static string ComputeRequestHash(object request)
    {
        string json = JsonSerializer.Serialize(request, SerializerOptions);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
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
