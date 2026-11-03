using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rose.Api.IntegrationTests;

public sealed class HabitsTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/habits/{path}", UriKind.Relative);

    private static string Day(int daysAgo) => DateTime.Today.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    [Fact]
    public async Task A_habit_can_be_created_ticked_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(Url("items"), new { name = "Read", icon = "B", kind = "check", remind_at = "21:30" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var habit = await created.JsonAsync();
        var id = habit.GetProperty("id").GetString();
        Assert.Equal("21:30", habit.GetProperty("remind_at").GetString());
        Assert.False(habit.GetProperty("done_today").GetBoolean());

        await client.PutAsJsonAsync(Url($"items/{id}/days/{Day(1)}"), new { value = 1 });
        var ticked = await (await client.PutAsJsonAsync(Url($"items/{id}/days/{Day(0)}"), new { value = 1 })).JsonAsync();
        Assert.Equal(2, ticked.GetProperty("current_streak").GetInt32());
        Assert.Equal(1, ticked.GetProperty("log").GetProperty(Day(0)).GetInt32());

        var list = await (await client.GetAsync(Url("items"))).JsonAsync();
        Assert.Contains(list.EnumerateArray(), h => h.GetProperty("id").GetString() == id);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"items/{id}"))).StatusCode);
        var gone = await client.PutAsJsonAsync(Url($"items/{id}/days/{Day(0)}"), new { value = 1 });
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("That habit no longer exists.", await gone.DetailAsync());
    }

    [Fact]
    public async Task Days_outside_the_last_week_are_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = (await (await client.PostAsJsonAsync(Url("items"), new { name = "Run" })).JsonAsync()).GetProperty("id").GetString();

        var old = await client.PutAsJsonAsync(Url($"items/{id}/days/{Day(7)}"), new { value = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Equal("Only the last 7 days can be changed.", await old.DetailAsync());
    }

    [Fact]
    public async Task The_popup_callbacks_work_with_the_body_it_sends()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = (await (await client.PostAsJsonAsync(Url("items"), new { name = "Water", kind = "count", target = 8, unit = "glasses" }))
            .JsonAsync()).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url($"items/{id}/nudge/snooze"), new { minutes = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url($"items/{id}/nudge/dismiss"), new { minutes = 0 })).StatusCode);
        var done = await (await client.PostAsJsonAsync(Url($"items/{id}/nudge/complete"), new { minutes = 0 })).JsonAsync();
        Assert.Equal(8, done.GetProperty("today").GetInt32());
    }

    [Fact]
    public async Task Habits_are_saved_to_habits_json()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = (await (await client.PostAsJsonAsync(Url("items"), new { name = "Stretch" })).JsonAsync()).GetProperty("id").GetString();
        await client.PutAsJsonAsync(Url($"items/{id}/days/{Day(0)}"), new { value = 1 });

        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "habits.json")));
        var saved = Assert.Single(file.RootElement.EnumerateArray(), h => h.GetProperty("id").GetString() == id);
        Assert.Equal(1, saved.GetProperty("log").GetProperty(Day(0)).GetInt32());
    }
}