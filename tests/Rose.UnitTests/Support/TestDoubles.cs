using Microsoft.Extensions.Logging.Abstractions;
using Rose.Application.Abstractions;
using Rose.Application.Users;
using Rose.Domain.Common;

namespace Rose.UnitTests.Support;

/// <summary>A clock the test controls: set <see cref="Now"/> or <see cref="Advance"/> it.</summary>
internal sealed class FakeClock(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Two people sharing one ROSE, and the admin.</summary>
internal static class TestUsers
{
    public const string AdminEmail = "admin@example.com";

    public static readonly Guid Alice = new("a11ce000-0000-4000-8000-000000000001");
    public static readonly Guid Bob = new("b0b00000-0000-4000-8000-000000000002");

    public static RoseUser AliceAccount { get; } = new(Alice, "alice@example.com", false, DateTimeOffset.UnixEpoch);

    public static RoseUser BobAccount { get; } = new(Bob, "bob@example.com", false, DateTimeOffset.UnixEpoch.AddDays(1));

    public static UserService Service(
        IUserStore store, IAccountProvider? accounts = null, TimeProvider? clock = null, params IUserScopedCache[] caches) =>
        new(store, accounts ?? new FakeAccounts(), new AccessPolicy([AdminEmail]), caches, clock ?? TimeProvider.System,
            NullLogger<UserService>.Instance);
}

/// <summary>
/// An in-memory <see cref="IRepository{T}"/> that counts writes. Every item is kept with its
/// owner; <see cref="Stored"/> is Alice's part of "the database".
/// </summary>
internal sealed class MemoryRepository<T>(Func<T, string> key) : IRepository<T>
    where T : class
{
    private readonly List<(Guid Owner, T Item)> rows = [];

    public List<T> Stored => StoredFor(TestUsers.Alice);

    public int Writes { get; private set; }

    public List<T> StoredFor(Guid owner) => [.. rows.Where(r => r.Owner == owner).Select(r => r.Item)];

    public Task<List<T>> LoadAllAsync(Guid owner, CancellationToken cancellationToken = default) => Task.FromResult(StoredFor(owner));

    public Task SaveAsync(Guid owner, IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            rows.RemoveAll(r => key(r.Item) == key(item));
            rows.Add((owner, item));
        }
        Writes++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid owner, string id, CancellationToken cancellationToken = default)
    {
        rows.RemoveAll(r => r.Owner == owner && key(r.Item) == id);
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>An in-memory accounts table. <see cref="Unowned"/> counts rows saved before accounts existed.</summary>
internal sealed class MemoryUserStore(params RoseUser[] users) : IUserStore
{
    public List<RoseUser> Users { get; } = [.. users];

    public List<Guid> DeletedWithData { get; } = [];

    public int Unowned { get; set; }

    public Guid? ClaimedBy { get; private set; }

    public bool FailAdds { get; set; }

    public Task<List<RoseUser>> LoadAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<List<RoseUser>>([.. Users]);

    public Task AddAsync(RoseUser user, CancellationToken cancellationToken = default)
    {
        if (FailAdds) throw new InvalidOperationException("database down");
        if (Users.Any(u => u.Id == user.Id || u.Email == user.Email)) throw new ConflictException("taken");
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SetMustChangePasswordAsync(Guid id, bool value, CancellationToken cancellationToken = default)
    {
        var index = Users.FindIndex(u => u.Id == id);
        if (index >= 0) Users[index] = Users[index] with { MustChangePassword = value };
        return Task.CompletedTask;
    }

    public Task<int> ClaimUnownedDataAsync(Guid owner, CancellationToken cancellationToken = default)
    {
        ClaimedBy = owner;
        var claimed = Unowned;
        Unowned = 0;
        return Task.FromResult(claimed);
    }

    public Task DeleteWithDataAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Users.RemoveAll(u => u.Id == id);
        DeletedWithData.Add(id);
        return Task.CompletedTask;
    }
}

/// <summary>Logins as Supabase would keep them: id -> (email, password).</summary>
internal sealed class FakeAccounts : IAccountProvider
{
    public bool IsConfigured { get; set; } = true;

    public Dictionary<Guid, (string Email, string Password)> Logins { get; } = [];

    public Task<Guid> CreateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Logins.Values.Any(l => l.Email == email)) throw new ConflictException("That email already has a login.");
        var id = Guid.NewGuid();
        Logins[id] = (email, password);
        return Task.FromResult(id);
    }

    public Task SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        Logins[id] = (Logins.TryGetValue(id, out var login) ? login.Email : "", password);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Logins.Remove(id);
        return Task.CompletedTask;
    }
}

/// <summary>Keeps every email instead of sending it.</summary>
internal sealed class RecordingEmail : IEmailSender
{
    public bool IsConfigured { get; set; } = true;

    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Records push payloads instead of sending them.</summary>
internal sealed class RecordingPush : IPushSender
{
    public List<(string Endpoint, string Payload)> Sent { get; } = [];

    public List<string> Payloads => [.. Sent.Select(s => s.Payload)];

    public string PublicKey => "key";

    public Task SendAsync(Rose.Application.Reminders.PushSubscriptionInfo subscription, string payload, CancellationToken cancellationToken)
    {
        Sent.Add((subscription.Endpoint!, payload));
        return Task.CompletedTask;
    }
}

internal static class Zones
{
    /// <summary>Berlin: DST starts on the last Sunday of March, ends on the last Sunday of October.</summary>
    public static TimeZoneInfo Berlin { get; } = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");
}