using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Persists replayable idempotency records in the relational publisher store.
/// </summary>
public sealed class EfIdempotencyStore(IDbContextFactory<KalshiIntegrationDbContext> dbContextFactory) : IIdempotencyStore
{
    private readonly IDbContextFactory<KalshiIntegrationDbContext> _dbContextFactory = dbContextFactory;

    public async Task<IdempotencyRecord?> GetAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        IdempotencyRecordEntity? entity = await dbContext.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        return entity is null
            ? null
            : new IdempotencyRecord(entity.Id, entity.Scope, entity.Key, entity.RequestHash, entity.StatusCode, entity.ResponseBody, entity.CreatedAt);
    }

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        await using KalshiIntegrationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        IdempotencyRecordEntity? entity = await dbContext.IdempotencyRecords.SingleOrDefaultAsync(x => x.Scope == record.Scope && x.Key == record.Key, cancellationToken);
        if (entity is null)
        {
            dbContext.IdempotencyRecords.Add(new IdempotencyRecordEntity
            {
                Id = record.Id,
                Scope = record.Scope,
                Key = record.Key,
                RequestHash = record.RequestHash,
                StatusCode = record.StatusCode,
                ResponseBody = record.ResponseBody,
                CreatedAt = record.CreatedAt,
            });
        }
        else
        {
            entity.RequestHash = record.RequestHash;
            entity.StatusCode = record.StatusCode;
            entity.ResponseBody = record.ResponseBody;
            entity.CreatedAt = record.CreatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
