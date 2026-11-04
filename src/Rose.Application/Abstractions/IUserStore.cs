using Rose.Application.Users;

namespace Rose.Application.Abstractions;

/// <summary>The ROSE accounts table, plus the clean-up that goes with removing an account.</summary>
public interface IUserStore
{
    Task<List<RoseUser>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an account. Throws <see cref="Domain.Common.ConflictException"/> when the id or email is taken.</summary>
    Task AddAsync(RoseUser user, CancellationToken cancellationToken = default);

    Task SetMustChangePasswordAsync(Guid id, bool value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives every row saved before ROSE had accounts (owner = <see cref="Guid.Empty"/>) to
    /// <paramref name="owner"/>. Returns how many rows moved.
    /// </summary>
    Task<int> ClaimUnownedDataAsync(Guid owner, CancellationToken cancellationToken = default);

    /// <summary>Deletes the account and every row it owns, in one transaction. Unknown ids are ignored.</summary>
    Task DeleteWithDataAsync(Guid id, CancellationToken cancellationToken = default);
}