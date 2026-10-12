using System.Net;
using System.Net.Http.Json;

namespace Rose.Api.IntegrationTests;

public sealed class RemindersTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/reminders/{path}", UriKind.Relative);

    [Fact]
    public async Task A_reminder_can_be_created_snoozed_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var due = DateTimeOffset.UtcNow.AddDays(1).ToString("O");

        var created = await client.PostAsJsonAsync(Url("items"), new { text = "Gym", due_at = due, repeat = "weekly", week_days = new[] { 1, 3 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reminder = await created.JsonAsync();
        var id = reminder.GetProperty("id").GetString();
        Assert.Equal(2, reminder.GetProperty("week_days").GetArrayLength());

        var snoozed = await client.PostAsync(Url($"items/{id}/snooze"), content: null);
        Assert.Equal(HttpStatusCode.OK, snoozed.StatusCode);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, (await snoozed.JsonAsync()).GetProperty("snoozed_until").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"items/{id}"))).StatusCode);
        var gone = await client.PostAsync(Url($"items/{id}/complete"), content: null);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("That reminder no longer exists.", await gone.DetailAsync());
    }

    [Fact]
    public async Task Bad_reminders_are_rejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Url("items"), new { text = "x", due_at = "soon", repeat = "none" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Pick a valid date and time.", await response.DetailAsync());
    }

    [Fact]
    public async Task Push_exposes_a_key_and_tracks_devices()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var key = (await (await client.GetAsync(Url("push/key"))).JsonAsync()).GetProperty("public_key").GetString();
        Assert.Equal(87, key!.Length); // a P-256 public key, base64url

        var subscription = new
        {
            endpoint = "https://push.example.invalid/device",
            expirationTime = (long?)null,
            keys = new { p256dh = key, auth = "c2VjcmV0c2VjcmV0MTI" },
        };
        var added = await (await client.PostAsJsonAsync(Url("push/subscribe"), new { subscription, label = "iPhone" })).JsonAsync();
        Assert.Equal(1, added.GetProperty("devices").GetInt32());

        var removed = await (await client.PostAsJsonAsync(Url("push/unsubscribe"), new { endpoint = subscription.endpoint })).JsonAsync();
        Assert.Equal(0, removed.GetProperty("devices").GetInt32());

        var test = await client.PostAsync(Url("push/test"), content: null);
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
    }
}