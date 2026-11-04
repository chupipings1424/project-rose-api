namespace Rose.Application.Abstractions;

/// <summary>
/// Loads and saves one kind of aggregate (a reminder, a habit with its days, a goal with its
/// entries...). The Repository pattern: services speak in domain objects and never see SQL.
/// </summary>
public interface IRepository<T>
    where T : class
{
    Task<List<T>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts new items and updates existing ones (children included), in one transaction.</summary>
    Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default);

    /// <summary>Deletes the item and its children. Unknown ids are ignored.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}