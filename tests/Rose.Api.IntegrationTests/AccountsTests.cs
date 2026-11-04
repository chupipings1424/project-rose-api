using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Rose.Infrastructure.Persistence;

namespace Rose.Api.IntegrationTests;

/// <summary>
/// Several people on one ROSE: the admin adds and removes accounts, a new account must change its
/// temporary password first, and nobody can see, change or download anybody else's data.
/// Each test gets its own database.
/// </summary>
public sealed class AccountsTests : IDisposable
{
    private readonly RoseApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private HttpClient Admin() => _factory.CreateClientWith(RoseApiFactory.Token());

    private HttpClient As(string email) => _factory.CreateClientWith(RoseApiFactory.Token(email));

    /// <summary>Adds an account and lets it pick its own password, so it can use ROSE.</summary>
    private async Task<HttpClient> AddMemberAsync(string email)
    {
        var created = await Admin().PostAsJsonAsync(Url("/api/admin/users"), new { email, temporary_password = "temporary1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = As(email);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Url("/api/auth/password"), new { new_password = "my-own-pass" })).StatusCode);
        return client;
    }

    [Fact]
    public async Task A_new_account_must_change_its_temporary_password_first()
    {
        var created = await (await Admin().PostAsJsonAsync(Url("/api/admin/users"), new { email = "Bob@Example.com", temporary_password = "temporary1" })).JsonAsync();
        Assert.Equal("bob@example.com", created.GetProperty("email").GetString());
        Assert.True(created.GetProperty("must_change_password").GetBoolean());
        var bob = As("bob@example.com");

        var session = await (await bob.GetAsync(Url("/api/auth/session"))).JsonAsync();
        Assert.True(session.GetProperty("must_change_password").GetBoolean());
        Assert.False(session.GetProperty("is_admin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync(Url("/api/reminders/items"))).StatusCode);

        var tooShort = await bob.PostAsJsonAsync(Url("/api/auth/password"), new { new_password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsJsonAsync(Url("/api/auth/password"), new { new_password = "my-own-pass" })).StatusCode);
        Assert.Equal("my-own-pass", _factory.Accounts.Logins[RoseApiFactory.UserIdFor("bob@example.com")].Password);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync(Url("/api/reminders/items"))).StatusCode);
        Assert.False((await (await bob.GetAsync(Url("/api/auth/session"))).JsonAsync()).GetProperty("must_change_password").GetBoolean());
    }

    [Fact]
    public async Task Only_admins_manage_accounts()
    {
        var bob = await AddMemberAsync("bob@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync(Url("/api/admin/users"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PostAsJsonAsync(Url("/api/admin/users"), new { email = "eve@example.com", temporary_password = "temporary1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url("/api/admin/users"))).StatusCode);

        var list = await (await Admin().GetAsync(Url("/api/admin/users"))).JsonAsync();
        Assert.Equal([RoseApiFactory.AdminEmail, "bob@example.com"], list.EnumerateArray().Select(u => u.GetProperty("email").GetString()));
    }

    [Fact]
    public async Task Adding_the_same_email_twice_conflicts()
    {
        await AddMemberAsync("bob@example.com");

        var again = await Admin().PostAsJsonAsync(Url("/api/admin/users"), new { email = "BOB@example.com", temporary_password = "temporary1" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("That email already has a ROSE account.", await again.DetailAsync());
    }

    [Fact]
    public async Task Nobody_sees_or_changes_anybody_elses_data()
    {
        var admin = Admin();
        var reminder = await (await admin.PostAsJsonAsync(Url("/api/reminders/items"),
            new { text = "Admin only", due_at = DateTimeOffset.UtcNow.AddDays(1).ToString("O"), repeat = "none" })).JsonAsync();
        var goal = await (await admin.PostAsJsonAsync(Url("/api/savings/goals"), new { name = "Car", target = 1000 })).JsonAsync();
        var habit = await (await admin.PostAsJsonAsync(Url("/api/habits/items"), new { name = "Read" })).JsonAsync();
        await admin.PostAsJsonAsync(Url("/api/payment-tracker/accounts"), new { title = "Rent", total_amount = "1000" });
        var bob = await AddMemberAsync("bob@example.com");

        Assert.Equal(0, (await (await bob.GetAsync(Url("/api/reminders/items"))).JsonAsync()).GetArrayLength());
        Assert.Equal(0, (await (await bob.GetAsync(Url("/api/savings/goals"))).JsonAsync()).GetArrayLength());
        Assert.Equal(0, (await (await bob.GetAsync(Url("/api/habits/items"))).JsonAsync()).GetArrayLength());
        Assert.Equal(0, (await (await bob.GetAsync(Url("/api/payment-tracker/accounts"))).JsonAsync()).GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url($"/api/reminders/items/{reminder.GetProperty("id").GetString()}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url($"/api/savings/goals/{goal.GetProperty("id").GetString()}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url($"/api/habits/items/{habit.GetProperty("id").GetString()}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url("/api/payment-tracker/accounts/Rent"))).StatusCode);

        // Same title, different person: no conflict.
        Assert.Equal(HttpStatusCode.Created,
            (await bob.PostAsJsonAsync(Url("/api/payment-tracker/accounts"), new { title = "Rent", total_amount = "5" })).StatusCode);
        Assert.Equal(1, (await (await admin.GetAsync(Url("/api/reminders/items"))).JsonAsync()).GetArrayLength());
        Assert.Equal(1, (await (await admin.GetAsync(Url("/api/payment-tracker/accounts"))).JsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task A_phone_belongs_to_whoever_subscribed_it_last()
    {
        var admin = Admin();
        var bob = await AddMemberAsync("bob@example.com");
        var subscription = new
        {
            endpoint = "https://push.example.invalid/shared-phone",
            expirationTime = (long?)null,
            keys = new { p256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM", auth = "c2VjcmV0c2VjcmV0MTI" },
        };

        await admin.PostAsJsonAsync(Url("/api/reminders/push/subscribe"), new { subscription, label = "Pixel" });
        var devices = await (await admin.GetAsync(Url("/api/reminders/push/devices"))).JsonAsync();
        Assert.Equal("Pixel", Assert.Single(devices.EnumerateArray()).GetProperty("label").GetString());

        await bob.PostAsJsonAsync(Url("/api/reminders/push/subscribe"), new { subscription, label = "Pixel" });

        Assert.Equal(0, (await (await admin.GetAsync(Url("/api/reminders/push/devices"))).JsonAsync()).GetArrayLength());
        Assert.Equal(1, (await (await bob.GetAsync(Url("/api/reminders/push/devices"))).JsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Removing_an_account_deletes_its_login_and_all_its_data()
    {
        var bob = await AddMemberAsync("bob@example.com");
        var bobId = RoseApiFactory.UserIdFor("bob@example.com");
        await bob.PostAsJsonAsync(Url("/api/reminders/items"), new { text = "Bob's", due_at = DateTimeOffset.UtcNow.AddDays(1).ToString("O"), repeat = "none" });
        await bob.PostAsJsonAsync(Url("/api/habits/items"), new { name = "Run" });
        await bob.PostAsJsonAsync(Url("/api/payment-tracker/accounts"), new { title = "Phone", total_amount = "300", instalments = "3" });

        Assert.Equal(HttpStatusCode.Conflict, (await Admin().DeleteAsync(Url($"/api/admin/users/{RoseApiFactory.UserIdFor(RoseApiFactory.AdminEmail)}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Admin().DeleteAsync(Url($"/api/admin/users/{bobId}"))).StatusCode);

        Assert.False(_factory.Accounts.Logins.ContainsKey(bobId));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync(Url("/api/reminders/items"))).StatusCode);
        await using var db = _factory.Database();
        Assert.Empty(await db.Reminders.Where(r => r.OwnerId == bobId).ToListAsync());
        Assert.Empty(await db.Habits.Where(h => h.OwnerId == bobId).ToListAsync());
        Assert.Empty(await db.Payments.Where(p => p.OwnerId == bobId).ToListAsync());
        Assert.Empty(await db.Users.Where(u => u.Id == bobId).ToListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Admin().DeleteAsync(Url($"/api/admin/users/{bobId}"))).StatusCode);
    }

    [Fact]
    public async Task The_admins_first_sign_in_takes_over_data_from_before_accounts()
    {
        await using (var db = _factory.Database())
        {
            db.Reminders.Add(new ReminderRow
            {
                Id = "legacy", Text = "From before", Repeat = "none", DueAt = DateTimeOffset.UtcNow.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var reminders = await (await Admin().GetAsync(Url("/api/reminders/items"))).JsonAsync();

        Assert.Equal("legacy", Assert.Single(reminders.EnumerateArray()).GetProperty("id").GetString());
        await using var check = _factory.Database();
        Assert.Equal(RoseApiFactory.UserIdFor(RoseApiFactory.AdminEmail), (await check.Reminders.SingleAsync()).OwnerId);
    }
}