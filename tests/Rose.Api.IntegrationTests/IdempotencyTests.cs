using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Rose.Application.Abstractions;

namespace Rose.Api.IntegrationTests;

/// <summary>An offline phone may send the same save twice; the Idempotency-Key makes the second one harmless.</summary>
public sealed class IdempotencyTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/{path}", UriKind.Relative);

    private static HttpRequestMessage Post(string path, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<double> MonthAmount(HttpClient client, string item)
    {
        var budget = await (await client.GetAsync(Url("payment-tracker/budget"))).JsonAsync();
        var month = DateTime.Now.Month - 1;
        foreach (var section in budget.GetProperty("sections").EnumerateArray())
        foreach (var group in section.GetProperty("groups").EnumerateArray())
        foreach (var line in group.GetProperty("lines").EnumerateArray())
            if (line.GetProperty("item").GetString() == item) return line.GetProperty("amounts")[month].GetDouble();
        return 0;
    }

    [Fact]
    public async Task Sending_an_expense_twice_with_the_same_key_adds_it_once()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var item = "Coffee " + Guid.NewGuid().ToString("N")[..6];
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(Post("payment-tracker/budget/expenses", new { item, amount = "50" }, key));
        var again = await client.SendAsync(Post("payment-tracker/budget/expenses", new { item, amount = "50" }, key));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(50, (await again.JsonAsync()).GetProperty("month_total").GetDouble());
        Assert.Equal(50, await MonthAmount(client, item));
    }

    [Fact]
    public async Task Different_keys_or_no_key_are_separate_saves()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var item = "Taxi " + Guid.NewGuid().ToString("N")[..6];

        await client.SendAsync(Post("payment-tracker/budget/expenses", new { item, amount = 10 }, Guid.NewGuid().ToString()));
        await client.SendAsync(Post("payment-tracker/budget/expenses", new { item, amount = 10 }, Guid.NewGuid().ToString()));
        await client.SendAsync(Post("payment-tracker/budget/expenses", new { item, amount = 10 }, null));

        Assert.Equal(30, await MonthAmount(client, item));
    }

    [Fact]
    public async Task A_replayed_payment_keeps_its_201_and_is_stored_once()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Gym " + Guid.NewGuid().ToString("N")[..6];
        var key = Guid.NewGuid().ToString();
        var body = new { title, amount = "500", date = "2026-11-05", status = "Paid" };

        var first = await client.SendAsync(Post("payment-tracker/payments", body, key));
        var again = await client.SendAsync(Post("payment-tracker/payments", body, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var summary = await (await client.GetAsync(Url($"payment-tracker/summary/{Uri.EscapeDataString(title)}"))).JsonAsync();
        Assert.Equal(1, summary.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task A_replayed_reminder_is_created_once()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var text = "Water plants " + Guid.NewGuid().ToString("N")[..6];
        var key = Guid.NewGuid().ToString();
        var body = new { text, due_at = DateTimeOffset.UtcNow.AddDays(1), repeat = "none" };

        await client.SendAsync(Post("reminders/items", body, key));
        await client.SendAsync(Post("reminders/items", body, key));

        var all = await (await client.GetAsync(Url("reminders/items"))).JsonAsync();
        Assert.Equal(1, all.EnumerateArray().Count(r => r.GetProperty("text").GetString() == text));
    }

    [Fact]
    public async Task Keys_belong_to_one_user()
    {
        _ = factory.Server; // runs the migrations
        var store = factory.Services.GetRequiredService<IIdempotencyStore>();
        var key = Guid.NewGuid().ToString();
        var alice = Guid.NewGuid();

        await store.SaveAsync(alice, key, new StoredResponse(200, "{}"));
        await store.SaveAsync(alice, key, new StoredResponse(201, "{\"second\":true}"));

        Assert.Equal(new StoredResponse(200, "{}"), await store.FindAsync(alice, key));
        Assert.Null(await store.FindAsync(Guid.NewGuid(), key));
    }
    [Fact]
    public async Task A_failed_save_is_not_remembered()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var key = Guid.NewGuid().ToString();

        var bad = await client.SendAsync(Post("payment-tracker/budget/expenses", new { item = "", amount = 5 }, key));
        var fixedUp = await client.SendAsync(Post("payment-tracker/budget/expenses", new { item = "Fixed " + key[..6], amount = 5 }, key));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
    }
}