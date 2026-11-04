namespace Rose.Application.Abstractions;

/// <summary>
/// Loads and saves one kind of aggregate (a reminder, a habit with its days, a goal with its
/// entries...) for one user. The Repository pattern: services speak in domain objects and never
/// see SQL. Every call names the <c>owner</c>, so one user's data can never leak into another's.
/// </summary>
public interface IRepository<T>
    where T : class
{
    Task<List<T>> LoadAllAsync(Guid owner, CancellationToken cancellationToken = default);

    /// <summary>Inserts new items and updates existing ones (children included), in one transaction.</summary>
    Task SaveAsync(Guid owner, IReadOnlyCollection<T> items, CancellationToken cancellationToken = default);

    /// <summary>Deletes the owner's item and its children. Unknown ids (or someone else's) are ignored.</summary>
    Task DeleteAsync(Guid owner, string id, CancellationToken cancellationToken = default);
}
