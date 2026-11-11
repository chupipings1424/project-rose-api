using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Rose.Api.IntegrationTests;

public sealed class LoansTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path = "") => new($"/api/loans{path}", UriKind.Relative);

    [Fact]
    public async Task A_loan_can_be_added_changed_reported_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(Url(), new
        {
            name = "Coop",
            type = "Coop",
            loan_date = "2020-04-02",
            term = 24,
            monthly_amortization = 500,
            amount = 10000,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var loan = await created.JsonAsync();
        var id = loan.GetProperty("id").GetString();
        Assert.Equal("completed", loan.GetProperty("status").GetString());
        Assert.Equal(24, loan.GetProperty("payments_made").GetInt32());
        Assert.Equal(0m, loan.GetProperty("balance").GetDecimal());
        Assert.Equal("2022-04-02", loan.GetProperty("finish_date").GetString());
        Assert.Equal(24, loan.GetProperty("schedule").GetArrayLength());
        Assert.Equal("2020-05-02", loan.GetProperty("schedule")[0].GetProperty("due_date").GetString());

        var changed = await client.PatchAsJsonAsync(Url($"/{id}"), new
        {
            name = "Coop",
            type = "Coop",
            loan_date = DateTime.Today.ToString("yyyy-MM-dd"),
            term = 12,
            monthly_amortization = 1000,
            amount = 10000,
        });
        var updated = await changed.JsonAsync();
        Assert.Equal("not-started", updated.GetProperty("status").GetString());
        Assert.Equal(12000m, updated.GetProperty("balance").GetDecimal());

        var report = await client.GetAsync(Url("/report"));
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", report.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await report.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"/{id}"))).StatusCode);
        var gone = await client.DeleteAsync(Url($"/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("That loan no longer exists.", await gone.DetailAsync());
    }

    [Fact]
    public async Task Loans_are_saved_to_the_database()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync(Url(), new
        {
            name = "Pag-IBIG MPL",
            type = "Pag-IBIG",
            loan_date = "2026-03-15",
            term = 24,
            monthly_amortization = 2345.67,
            amount = 50000,
        });
        var id = (await created.JsonAsync()).GetProperty("id").GetString();

        await using var db = factory.Database();
        var saved = await db.Loans.SingleAsync(l => l.Id == id);
        Assert.Equal("Pag-IBIG", saved.Type);
        Assert.Equal(new DateOnly(2026, 3, 15), saved.LoanDate);
        Assert.Equal(2345.67m, saved.MonthlyAmortization);
    }

    [Fact]
    public async Task Bad_input_is_rejected_with_a_friendly_message()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Url(), new { name = "Coop", loan_date = "2026-04-02", term = 0, monthly_amortization = 500, amount = 10000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The term must be at least 1 month.", await response.DetailAsync());
    }

    [Fact]
    public async Task Types_are_listed()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var types = await client.GetFromJsonAsync<string[]>(Url("/types"));
        Assert.Contains("Pag-IBIG", types!);
    }
}