using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rose.Api.IntegrationTests;

public sealed class SavingsTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/savings/{path}", UriKind.Relative);

    [Fact]
    public async Task A_goal_can_be_created_funded_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var deadline = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");

        var created = await client.PostAsJsonAsync(Url("goals"), new { name = "Japan trip", icon = "J", target = 80000, deadline });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.JsonAsync()).GetProperty("id").GetString();

        var funded = await client.PostAsJsonAsync(Url($"goals/{id}/entries"), new { kind = "deposit", amount = 2000.5, note = "Bonus" });
        Assert.Equal(HttpStatusCode.Created, funded.StatusCode);
        var goal = await funded.JsonAsync();
        Assert.Equal(2000.5m, goal.GetProperty("saved").GetDecimal());
        Assert.Equal(deadline, goal.GetProperty("deadline").GetString());
        Assert.Equal(JsonValueKind.Number, goal.GetProperty("monthly_needed").ValueKind);
        Assert.Equal("Bonus", goal.GetProperty("entries")[0].GetProperty("note").GetString());

        var list = await (await client.GetAsync(Url("goals"))).JsonAsync();
        Assert.Contains(list.EnumerateArray(), g => g.GetProperty("id").GetString() == id);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"goals/{id}"))).StatusCode);
        var gone = await client.PostAsJsonAsync(Url($"goals/{id}/entries"), new { amount = 1 });
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("That goal no longer exists.", await gone.DetailAsync());
    }

    [Fact]
    public async Task Goals_are_saved_to_goals_json()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(Url("goals"), new { name = "Emergency fund", target = 1000 });

        var saved = await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "goals.json"));
        Assert.Contains("Emergency fund", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overdrawing_is_rejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync(Url("goals"), new { name = "Bike", target = 500 });
        var id = (await created.JsonAsync()).GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync(Url($"goals/{id}/entries"), new { kind = "withdraw", amount = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("You can withdraw at most 0.", await response.DetailAsync());
    }
}