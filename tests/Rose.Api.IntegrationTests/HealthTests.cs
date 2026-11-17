using System.Net;
using System.Net.Http.Json;

namespace Rose.Api.IntegrationTests;

/// <summary>The Health Tracker is for admins only; "My apps" is for everybody and survives a new device.</summary>
public sealed class HealthTests : IDisposable
{
    private readonly RoseApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private HttpClient Admin() => _factory.CreateClientWith(RoseApiFactory.Token());

    private async Task<HttpClient> MemberAsync(string email)
    {
        var created = await Admin().PostAsJsonAsync(Url("/api/admin/users"), new { email, temporary_password = "temporary1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = _factory.CreateClientWith(RoseApiFactory.Token(email));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Url("/api/auth/password"), new { new_password = "my-own-pass" })).StatusCode);
        return client;
    }

    [Fact]
    public async Task The_admin_logs_meals_fasts_walks_and_weight()
    {
        var admin = Admin();
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        var profile = await admin.PutAsJsonAsync(Url("/api/health/profile"), new { height_cm = 176, birth_year = 1990, sex = "male", goal_kg = 75 });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        var day = await admin.PutAsJsonAsync(Url($"/api/health/days/{today}"), new { weight_kg = 80, walk_km = 5, fasting_hours = 16 });
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);

        var meal = await admin.PostAsJsonAsync(Url($"/api/health/days/{today}/meals"), new { name = "Chicken adobo", kcal = 450 });
        var overview = await meal.JsonAsync();
        var log = overview.GetProperty("today_log");
        Assert.Equal(450, log.GetProperty("kcal_in").GetInt32());
        Assert.Equal(240, log.GetProperty("walk_kcal").GetInt32());
        Assert.Equal("16:8", log.GetProperty("fasting_label").GetString());
        Assert.Equal(1, overview.GetProperty("fasting_streak").GetInt32());
        Assert.True(overview.GetProperty("profile").GetProperty("complete").GetBoolean());
        Assert.Equal(30, overview.GetProperty("days").GetArrayLength());

        var mealId = log.GetProperty("meals")[0].GetProperty("id").GetString();
        var removed = await (await admin.DeleteAsync(Url($"/api/health/days/{today}/meals/{mealId}"))).JsonAsync();
        Assert.Equal(0, removed.GetProperty("today_log").GetProperty("kcal_in").GetInt32());

        var week = await (await admin.GetAsync(Url("/api/health?days=7"))).JsonAsync();
        Assert.Equal(7, week.GetProperty("days").GetArrayLength());

        var bad = await admin.PostAsJsonAsync(Url($"/api/health/days/{today}/meals"), new { name = "", kcal = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Members_cannot_reach_the_health_tracker()
    {
        var bob = await MemberAsync("bob@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync(Url("/api/health"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PutAsJsonAsync(Url($"/api/health/days/{DateTime.Today:yyyy-MM-dd}"), new { walk_km = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url("/api/health"))).StatusCode);
    }

    [Fact]
    public async Task My_apps_are_saved_per_person()
    {
        var admin = Admin();
        var bob = await MemberAsync("bob@example.com");

        var saved = await bob.PutAsJsonAsync(Url("/api/me/apps"), new { hidden = new[] { "savings", "habits" } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var again = await (await _factory.CreateClientWith(RoseApiFactory.Token("bob@example.com")).GetAsync(Url("/api/me/apps"))).JsonAsync();
        Assert.Equal(["habits", "savings"], again.GetProperty("hidden").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, (await (await admin.GetAsync(Url("/api/me/apps"))).JsonAsync()).GetProperty("hidden").GetArrayLength());

        var bad = await bob.PutAsJsonAsync(Url("/api/me/apps"), new { hidden = new[] { "Not OK!" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var bobId = RoseApiFactory.UserIdFor("bob@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Url($"/api/admin/users/{bobId}"))).StatusCode);
        using var db = _factory.Database();
        Assert.DoesNotContain(db.Set<Rose.Infrastructure.Persistence.AppPrefsRow>(), p => p.OwnerId == bobId);
    }
}