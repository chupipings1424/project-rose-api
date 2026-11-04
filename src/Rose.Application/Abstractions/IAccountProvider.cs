namespace Rose.Application.Abstractions;

/// <summary>
/// The sign-in service's admin side (Supabase Auth): create and remove logins, set passwords.
/// Needs a server-only secret key; without it <see cref="IsConfigured"/> is false.
/// </summary>
public interface IAccountProvider
{
    bool IsConfigured { get; }

    /// <summary>Creates a confirmed login and returns its id. Conflict when the email already has one.</summary>
    Task<Guid> CreateAsync(string email, string password, CancellationToken cancellationToken = default);

    Task SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken = default);

    /// <summary>Deletes the login. An id that no longer exists is not an error.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}