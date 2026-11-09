using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rose.Application.Abstractions;

namespace Rose.Infrastructure.Persistence;

/// <summary>The rose.idempotency_keys table. Answers are kept for <see cref="KeepFor"/>, then tidied away.</summary>
internal sealed class PostgresIdempotencyStore(IDbContextFactory<RoseDbContext> contexts, TimeProvider clock) : IIdempotencyStore
{
    /// <summary>Far longer than a phone stays offline; after that a key is forgotten.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public async Task<StoredResponse?> FindAsync(Guid owner, string key, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OwnerId == owner && r.Key == key, cancellationToken);
        return row is null ? null : new StoredResponse(row.StatusCode, row.Body);
    }

    public async Task SaveAsync(Guid owner, string key, StoredResponse response, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var expired = now - KeepFor;
        await db.IdempotencyKeys.Where(r => r.OwnerId == owner && r.CreatedAt < expired).ExecuteDeleteAsync(cancellationToken);

        db.IdempotencyKeys.Add(new IdempotencyKeyRow
        {
            OwnerId = owner,
            Key = key,
            StatusCode = response.StatusCode,
            Body = response.Body,
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two copies of the same request raced; the first answer stays.
        }
    }
}