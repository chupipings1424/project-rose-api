using Rose.Application.Abstractions;

namespace Rose.Application.Common;

/// <summary>
/// Keeps every item of one repository in memory (ROSE has one user and small tables) and lets
/// one caller at a time read or change them. Changes are written to the database straight away
/// (write-through), so a restart loses nothing.
/// </summary>
/// <remarks>
/// Only correct while a single API instance owns the database, which is how ROSE is hosted.
/// If a save fails the cache is dropped and reloaded on next use, so memory never drifts from
/// what is really stored.
/// </remarks>
public sealed class RepositoryCache<T>(IRepository<T> repository) : IDisposable
    where T : class
{
    private readonly IRepository<T> store = repository;
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<T>? items;

    /// <summary>Waits for exclusive access. Dispose the session to let the next caller in.</summary>
    public async Task<Session> OpenAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            items ??= await store.LoadAllAsync(cancellationToken);
            return new Session(this, items);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public void Dispose() => gate.Dispose();

    /// <summary>Exclusive access to the cached items until disposed.</summary>
    public sealed class Session : IDisposable
    {
        private readonly RepositoryCache<T> owner;
        private bool disposed;

        internal Session(RepositoryCache<T> owner, List<T> items)
        {
            this.owner = owner;
            Items = items;
        }

        public List<T> Items { get; }

        public Task SaveAsync(T item, CancellationToken cancellationToken = default) => SaveAsync([item], cancellationToken);

        public Task SaveAsync(IReadOnlyCollection<T> changed, CancellationToken cancellationToken = default) =>
            Guard(() => owner.store.SaveAsync(changed, cancellationToken));

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Guard(() => owner.store.DeleteAsync(id, cancellationToken));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.gate.Release();
        }

        private async Task Guard(Func<Task> write)
        {
            try
            {
                await write();
            }
            catch
            {
                owner.items = null;
                throw;
            }
        }
    }
}