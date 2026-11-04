using Rose.Application.Abstractions;
using Rose.Application.Users;
using Rose.Domain.Common;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class UserServiceTests
{
    private static readonly Guid AdminId = new("adadadad-0000-4000-8000-000000000003");
    private readonly MemoryUserStore _store = new(TestUsers.AliceAccount);
    private readonly FakeAccounts _accounts = new();

    private UserService Service(params IUserScopedCache[] caches) => TestUsers.Service(_store, _accounts, caches: caches);

    [Fact]
    public async Task An_admins_first_sign_in_creates_the_account_and_claims_old_data()
    {
        _store.Unowned = 42;
        var users = Service();

        var account = await users.ResolveAsync(AdminId, "Admin@Example.com");

        Assert.NotNull(account);
        Assert.True(account.IsAdmin);
        Assert.Equal("admin@example.com", account.User.Email);
        Assert.Equal(AdminId, _store.ClaimedBy);
        Assert.Contains(_store.Users, u => u.Id == AdminId);
    }

    [Fact]
    public async Task Someone_without_an_account_is_refused()
    {
        var users = Service();

        Assert.Null(await users.ResolveAsync(Guid.NewGuid(), "stranger@example.com"));
        Assert.Null(await users.ResolveAsync(Guid.NewGuid(), null));
        Assert.Null(_store.ClaimedBy);
    }

    [Fact]
    public async Task Members_are_found_by_id_not_by_email()
    {
        var users = Service();

        var alice = await users.ResolveAsync(TestUsers.Alice, "whatever@example.com");
        Assert.NotNull(alice);
        Assert.False(alice.IsAdmin);
        Assert.Null(await users.ResolveAsync(Guid.NewGuid(), "alice@example.com"));
    }

    [Fact]
    public async Task An_admin_email_with_a_new_id_is_refused_so_their_data_stays_safe()
    {
        var users = Service();
        await users.ResolveAsync(AdminId, TestUsers.AdminEmail);

        Assert.Null(await users.ResolveAsync(Guid.NewGuid(), TestUsers.AdminEmail));
    }

    [Fact]
    public async Task Adding_a_user_creates_a_login_that_must_change_its_password()
    {
        var users = Service();

        var view = await users.CreateAsync(new CreateUserRequest(" Bob@Example.com ", "temporary1"));

        Assert.Equal("bob@example.com", view.Email);
        Assert.True(view.MustChangePassword);
        Assert.Equal(("bob@example.com", "temporary1"), _accounts.Logins[view.Id]);
        var account = await users.ResolveAsync(view.Id, "bob@example.com");
        Assert.True(account!.User.MustChangePassword);
    }

    [Theory]
    [InlineData("not-an-email", "temporary1")]
    [InlineData("bob@example", "temporary1")]
    [InlineData("bob@example.com", "short")]
    [InlineData("bob@example.com", " padded-password ")]
    public async Task Bad_emails_and_passwords_are_rejected(string email, string password)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(new CreateUserRequest(email, password)));
        Assert.Empty(_accounts.Logins);
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("ADMIN@example.com")]
    public async Task Existing_and_admin_emails_cannot_be_added(string email)
    {
        await Assert.ThrowsAsync<ConflictException>(() => Service().CreateAsync(new CreateUserRequest(email, "temporary1")));
        Assert.Empty(_accounts.Logins);
    }

    [Fact]
    public async Task A_failed_save_removes_the_new_login_again()
    {
        _store.FailAdds = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CreateAsync(new CreateUserRequest("bob@example.com", "temporary1")));

        Assert.Empty(_accounts.Logins);
    }

    [Fact]
    public async Task Without_the_secret_key_accounts_cannot_be_managed()
    {
        _accounts.IsConfigured = false;

        await Assert.ThrowsAsync<UnavailableException>(() => Service().CreateAsync(new CreateUserRequest("bob@example.com", "temporary1")));
    }

    [Fact]
    public async Task Changing_your_password_ends_the_temporary_one()
    {
        var users = Service();
        var bob = await users.CreateAsync(new CreateUserRequest("bob@example.com", "temporary1"));

        await users.ChangeOwnPasswordAsync(bob.Id, new ChangePasswordRequest("my-own-secret"));

        Assert.Equal("my-own-secret", _accounts.Logins[bob.Id].Password);
        Assert.False((await users.FindAsync(bob.Id))!.MustChangePassword);
        Assert.False(_store.Users.Single(u => u.Id == bob.Id).MustChangePassword);
    }

    [Fact]
    public async Task Resetting_a_password_makes_them_change_it_again()
    {
        var users = Service();

        var view = await users.ResetPasswordAsync(TestUsers.Alice, new ResetPasswordRequest("temporary2"));

        Assert.True(view.MustChangePassword);
        Assert.Equal("temporary2", _accounts.Logins[TestUsers.Alice].Password);
    }

    [Fact]
    public async Task Removing_a_user_deletes_the_login_the_data_and_the_caches()
    {
        var cache = new RecordingCache();
        var users = Service(cache);
        _accounts.Logins[TestUsers.Alice] = ("alice@example.com", "whatever1");
        await users.ResolveAsync(AdminId, TestUsers.AdminEmail);

        await users.RemoveAsync(AdminId, TestUsers.Alice);

        Assert.Empty(_accounts.Logins);
        Assert.Equal([TestUsers.Alice], _store.DeletedWithData);
        Assert.Equal([TestUsers.Alice], cache.Forgotten);
        Assert.Null(await users.ResolveAsync(TestUsers.Alice, "alice@example.com"));
        await Assert.ThrowsAsync<NotFoundException>(() => users.RemoveAsync(AdminId, TestUsers.Alice));
    }

    [Fact]
    public async Task Admins_and_yourself_cannot_be_removed()
    {
        var users = Service();
        await users.ResolveAsync(AdminId, TestUsers.AdminEmail);

        await Assert.ThrowsAsync<ConflictException>(() => users.RemoveAsync(AdminId, AdminId));
        await Assert.ThrowsAsync<ConflictException>(() => users.RemoveAsync(TestUsers.Alice, AdminId));
        Assert.Empty(_store.DeletedWithData);
    }

    [Fact]
    public async Task The_list_shows_admins_first()
    {
        var users = Service();
        await users.ResolveAsync(AdminId, TestUsers.AdminEmail);

        var list = await users.ListAsync();

        Assert.Equal(["admin@example.com", "alice@example.com"], list.Select(u => u.Email));
        Assert.True(list[0].IsAdmin);
    }

    private sealed class RecordingCache : IUserScopedCache
    {
        public List<Guid> Forgotten { get; } = [];

        public void Forget(Guid owner) => Forgotten.Add(owner);
    }
}