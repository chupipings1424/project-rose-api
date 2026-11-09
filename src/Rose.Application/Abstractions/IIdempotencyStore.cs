namespace Rose.Application.Abstractions;

/// <summary>
/// Remembers the answer to each request sent with an <c>Idempotency-Key</c>. A phone that
/// retries after a dropped connection gets the first answer back instead of saving twice.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>The answer already given for <paramref name="key"/>, or null if it is new.</summary>
    Task<StoredResponse?> FindAsync(Guid owner, string key, CancellationToken cancellationToken = default);

    /// <summary>Keeps the answer for a while. A key that is already stored is left as it is.</summary>
    Task SaveAsync(Guid owner, string key, StoredResponse response, CancellationToken cancellationToken = default);
}

/// <param name="Body">The JSON that was sent back.</param>
public sealed record StoredResponse(int StatusCode, string Body);