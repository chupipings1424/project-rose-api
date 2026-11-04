using System.Collections.Concurrent;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Domain.Common;

namespace Rose.Application.Users;

/// <summary>
/// ROSE accounts: who may sign in, adding and removing people (admins only), and passwords.
/// A singleton that keeps the small accounts table in memory, so checking every request is cheap.
/// </summary>
public sealed partial class UserService(
    IUserStore store,
    IAccountProvider accounts,
    AccessPolicy access,
    IEnumerable<IUserScopedCache> caches,
    TimeProvider clock,
    ILogger<UserService> logger) : IDisposable
{
    public const int MinPasswordLength = 8;

    // Supabase stores passwords with bcrypt, which only reads the first 72 bytes.
    public const int MaxPasswordLength = 72;

    private readonly SemaphoreSlim gate = new(1, 1);
    private ConcurrentDictionary<Guid, RoseUser>? users;

    /// <summary>
    /// Is this signed-in Supabase user allowed into ROSE? Admins always are: the first time an
    /// admin signs in they get an account, plus every row saved before ROSE had accounts.
    /// Returns null for anybody else without an account.
    /// </summary>
    public async Task<Account?> ResolveAsync(Guid id, string? email, CancellationToken ct = default)
    {
        var all = await UsersAsync(ct);
        if (all.TryGetValue(id, out var known)) return new Account(known, access.IsAdmin(known.Email));
        if (!access.IsAdmin(email) || id == Guid.Empty) return null;

        await gate.WaitAsync(ct);
        try
        {
            if (users!.TryGetValue(id, out known)) return new Account(known, true);
            if (users.Values.Any(u => SameEmail(u.Email, email!)))
            {
                LogAdminIdChanged(email!);
                return null;
            }
            var admin = new RoseUser(id, Normalize(email!), false, clock.GetUtcNow());
            await store.AddAsync(admin, ct);
            var claimed = await store.ClaimUnownedDataAsync(id, ct);
            users[id] = admin;
            LogAdminAdded(admin.Email, claimed);
            return new Account(admin, true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Everybody with an account (the scheduler rings reminders for each of them).</summary>
    public async Task<IReadOnlyList<RoseUser>> AllAsync(CancellationToken ct = default) =>
        [.. (await UsersAsync(ct)).Values.OrderBy(u => u.CreatedAt)];

    public async Task<RoseUser?> FindAsync(Guid id, CancellationToken ct = default) =>
        (await UsersAsync(ct)).GetValueOrDefault(id);

    /// <summary>The admin page's list: admins first, then everybody else by when they were added.</summary>
    public async Task<IReadOnlyList<UserView>> ListAsync(CancellationToken ct = default) =>
        [.. (await AllAsync(ct))
            .Select(u => new UserView(u.Id, u.Email, access.IsAdmin(u.Email), u.MustChangePassword, u.CreatedAt))
            .OrderByDescending(u => u.IsAdmin)
            .ThenBy(u => u.CreatedAt)];

    /// <summary>Creates the login (Supabase) and the ROSE account. They must change the password at first sign-in.</summary>
    public async Task<UserView> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = ParseEmail(request.Email);
        var password = ParsePassword(request.TemporaryPassword, "temporary password");
        RequireAccounts();
        if (access.IsAdmin(email)) throw new ConflictException("That email is an admin - it can already sign in.");

        await UsersAsync(ct);
        await gate.WaitAsync(ct);
        try
        {
            if (users!.Values.Any(u => SameEmail(u.Email, email)))
                throw new ConflictException("That email already has a ROSE account.");

            var id = await accounts.CreateAsync(email, password, ct);
            var user = new RoseUser(id, email, true, clock.GetUtcNow());
            try
            {
                await store.AddAsync(user, ct);
            }
            catch
            {
                // Don't leave a login behind that no account matches.
                await accounts.DeleteAsync(id, CancellationToken.None);
                throw;
            }
            users[id] = user;
            LogUserAdded(email);
            return new UserView(id, email, false, true, user.CreatedAt);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Sets a new temporary password (e.g. they forgot theirs); they must change it again.</summary>
    public async Task<UserView> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var password = ParsePassword(request.TemporaryPassword, "temporary password");
        RequireAccounts();
        var user = await RequireMemberAsync(id, ct);
        if (access.IsAdmin(user.Email)) throw new ConflictException("Admins change their own password.");

        await accounts.SetPasswordAsync(id, password, ct);
        var updated = await SetMustChangeAsync(user, true, ct);
        return new UserView(updated.Id, updated.Email, false, true, updated.CreatedAt);
    }

    /// <summary>The signed-in user picks a new password; this also ends "must change password".</summary>
    public async Task ChangeOwnPasswordAsync(Guid id, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var password = ParsePassword(request.NewPassword, "new password");
        RequireAccounts();
        var user = await RequireMemberAsync(id, ct);

        await accounts.SetPasswordAsync(id, password, ct);
        if (user.MustChangePassword) await SetMustChangeAsync(user, false, ct);
    }

    /// <summary>Deletes the login, the account and everything it owns. Admins and yourself can't be removed.</summary>
    public async Task RemoveAsync(Guid actingUser, Guid id, CancellationToken ct = default)
    {
        if (id == actingUser) throw new ConflictException("You can't remove your own account.");
        RequireAccounts();
        var user = await RequireMemberAsync(id, ct);
        if (access.IsAdmin(user.Email)) throw new ConflictException("Admins can't be removed here - take them out of Access:AdminEmails first.");

        // Login first, so they can't sign in again while the data goes. Safe to retry if a step fails.
        await accounts.DeleteAsync(id, ct);
        await store.DeleteWithDataAsync(id, ct);
        await gate.WaitAsync(ct);
        try
        {
            users!.TryRemove(id, out _);
        }
        finally
        {
            gate.Release();
        }
        foreach (var cache in caches) cache.Forget(id);
        LogUserRemoved(user.Email);
    }

    public void Dispose() => gate.Dispose();

    private async Task<ConcurrentDictionary<Guid, RoseUser>> UsersAsync(CancellationToken ct)
    {
        if (users is { } loaded) return loaded;
        await gate.WaitAsync(ct);
        try
        {
            return users ??= new ConcurrentDictionary<Guid, RoseUser>((await store.LoadAllAsync(ct)).Select(u => KeyValuePair.Create(u.Id, u)));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<RoseUser> RequireMemberAsync(Guid id, CancellationToken ct) =>
        await FindAsync(id, ct) ?? throw new NotFoundException("That account no longer exists.");

    private async Task<RoseUser> SetMustChangeAsync(RoseUser user, bool value, CancellationToken ct)
    {
        await store.SetMustChangePasswordAsync(user.Id, value, ct);
        var updated = user with { MustChangePassword = value };
        await gate.WaitAsync(ct);
        try
        {
            users![user.Id] = updated;
        }
        finally
        {
            gate.Release();
        }
        return updated;
    }

    private void RequireAccounts()
    {
        if (!accounts.IsConfigured)
            throw new UnavailableException("Managing accounts needs the Supabase secret key on the server (Supabase:SecretKey).");
    }

    private static string ParseEmail(string? value)
    {
        var email = (value ?? "").Trim();
        if (email.Length is 0 or > 254 || email.Contains(' ', StringComparison.Ordinal)
            || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new ValidationException("Enter a valid email address.");
        }
        return Normalize(email);
    }

    private static string ParsePassword(string? value, string what)
    {
        var password = value ?? "";
        if (password.Length < MinPasswordLength)
            throw new ValidationException($"The {what} needs at least {MinPasswordLength} characters.");
        if (System.Text.Encoding.UTF8.GetByteCount(password) > MaxPasswordLength)
            throw new ValidationException($"The {what} is too long (at most {MaxPasswordLength} characters).");
        if (password.Trim().Length != password.Length)
            throw new ValidationException($"The {what} can't start or end with a space.");
        return password;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static bool SameEmail(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {Email} signed in for the first time; {Rows} existing rows are now theirs")]
    private partial void LogAdminAdded(string email, int rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {Email} signed in with a new Supabase id; refused so their data stays safe")]
    private partial void LogAdminIdChanged(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account added: {Email}")]
    private partial void LogUserAdded(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account removed with all its data: {Email}")]
    private partial void LogUserRemoved(string email);
}