using System.Collections.Concurrent;
using Rose.Application.Abstractions;

namespace Rose.Application.Common;

/// <summary>
/// Keeps each user's items of one repository in memory (ROSE has few users and small tables) and
/// lets one caller at a time read or change one user's items. Changes are written to the database
/// straight away (write-through), so a restart loses nothing.
/// </summary>
/// <remarks>
/// Only correct while a single API instance owns the database, which is how ROSE is hosted.
/// If a save fails that user's cache is dropped and reloaded on next use, so memory never drifts
/// from what is really stored.
/// </remarks>
public sealed class RepositoryCache<T>(IRepository<T> repository) : IDisposable
    where T : class
{
    private readonly IRepository<T> store = repository;
    private readonly ConcurrentDictionary<Guid, Partition> partitions = new();

    /// <summary>Waits for exclusive access to one user's items. Dispose the session to let the next caller in.</summary>
    public async Task<Session> OpenAsync(Guid owner, CancellationToken cancellationToken = default)
    {
        var partition = partitions.GetOrAdd(owner, _ => new Partition());
        await partition.Gate.WaitAsync(cancellationToken);
        try
        {
            partition.Items ??= await store.LoadAllAsync(owner, cancellationToken);
            return new Session(this, owner, partition);
        }
        catch
        {
            partition.Gate.Release();
            throw;
        }
    }

    /// <summary>Drops a user's items from memory (after their account and data were deleted).</summary>
    public void Forget(Guid owner) => partitions.TryRemove(owner, out _);

    /// <summary>
    /// Makes every other user whose cached items contain a match reload from the database on next
    /// use - e.g. after a shared device moved from one user to another.
    /// </summary>
    public async Task ReloadOthersHoldingAsync(Guid owner, Func<T, bool> match, CancellationToken cancellationToken = default)
    {
        foreach (var (key, partition) in partitions)
        {
            if (key == owner) continue;
            await partition.Gate.WaitAsync(cancellationToken);
            try
            {
                if (partition.Items?.Any(match) == true) partition.Items = null;
            }
            finally
            {
                partition.Gate.Release();
            }
        }
    }

    public void Dispose()
    {
        foreach (var partition in partitions.Values) partition.Gate.Dispose();
        partitions.Clear();
    }

    internal sealed class Partition
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<T>? Items { get; set; }
    }

    /// <summary>Exclusive access to one user's cached items until disposed.</summary>
    public sealed class Session : IDisposable
    {
        private readonly RepositoryCache<T> owner;
        private readonly Partition partition;
        private bool disposed;

        internal Session(RepositoryCache<T> owner, Guid userId, Partition partition)
        {
            this.owner = owner;
            this.partition = partition;
            UserId = userId;
            Items = partition.Items!;
        }

        /// <summary>Whose items these are.</summary>
        public Guid UserId { get; }

        public List<T> Items { get; }

        public Task SaveAsync(T item, CancellationToken cancellationToken = default) => SaveAsync([item], cancellationToken);

        public Task SaveAsync(IReadOnlyCollection<T> changed, CancellationToken cancellationToken = default) =>
            Guard(() => owner.store.SaveAsync(UserId, changed, cancellationToken));

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Guard(() => owner.store.DeleteAsync(UserId, id, cancellationToken));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            partition.Gate.Release();
        }

        private async Task Guard(Func<Task> write)
        {
            try
            {
                await write();
            }
            catch
            {
                partition.Items = null;
                throw;
            }
        }
    }
}
