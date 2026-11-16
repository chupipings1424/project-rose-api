using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rose.Api.IntegrationTests;

public sealed class PaymentTrackerTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new($"/api/payment-tracker/{path}", UriKind.Relative);

    [Fact]
    public async Task Titles_with_a_slash_work_end_to_end()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Car/Loan " + Guid.NewGuid().ToString("N")[..6];
        var encoded = Uri.EscapeDataString(title);

        var created = await client.PostAsJsonAsync(Url("accounts"), new
        {
            title, total_amount = 1200, instalments = "3", frequency = "Monthly", start_date = "2026-01-31",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var summary = await (await client.GetAsync(Url($"summary/{encoded}"))).JsonAsync();
        Assert.Equal(title, summary.GetProperty("title").GetString());
        var payments = summary.GetProperty("payments");
        Assert.Equal(3, payments.GetArrayLength());

        var id = payments[0].GetProperty("id").GetString();
        var patched = await client.PatchAsJsonAsync(Url($"payments/{id}"), new { status = "Paid" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.True((await patched.JsonAsync()).GetProperty("ok").GetBoolean());

        var deleted = await (await client.DeleteAsync(Url($"accounts/{encoded}"))).JsonAsync();
        Assert.Equal(3, deleted.GetProperty("deleted_payments").GetInt32());

        // Like Flask: an unknown title gives an empty summary, and deleting it again is a 404.
        var after = await (await client.GetAsync(Url($"summary/{encoded}"))).JsonAsync();
        Assert.Equal(0, after.GetProperty("payments").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url($"accounts/{encoded}"))).StatusCode);
    }

    [Fact]
    public async Task A_bill_is_saved_with_its_kind_and_has_monthly_totals()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Water " + Guid.NewGuid().ToString("N")[..6];
        var encoded = Uri.EscapeDataString(title);

        var created = await client.PostAsJsonAsync(Url("accounts"), new { title, kind = "bill", notes = "Maynilad" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await client.PostAsJsonAsync(Url("payments"), new
        {
            title, amount = "480", date = "2026-01-10", time = "10:30", status = "Paid", due_date = "", note = "",
        });

        var accounts = await (await client.GetAsync(Url("accounts"))).JsonAsync();
        var account = accounts.EnumerateArray().Single(a => a.GetProperty("title").GetString() == title);
        Assert.Equal("bill", account.GetProperty("kind").GetString());

        var summary = await (await client.GetAsync(Url($"summary/{encoded}"))).JsonAsync();
        Assert.Equal("bill", summary.GetProperty("kind").GetString());
        Assert.False(summary.GetProperty("has_plan").GetBoolean());
        Assert.Equal(0, summary.GetProperty("remaining").GetDouble());
        Assert.Equal(480, summary.GetProperty("total_paid").GetDouble());
        Assert.Equal(12, summary.GetProperty("monthly").GetArrayLength());

        await client.DeleteAsync(Url($"accounts/{encoded}"));
    }

    [Fact]
    public async Task Paying_a_scheduled_instalment_settles_it_instead_of_adding_a_row()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Phone " + Guid.NewGuid().ToString("N")[..6];
        await client.PostAsJsonAsync(Url("accounts"), new
        {
            title, total_amount = 300, instalments = "3", frequency = "Monthly", start_date = "2026-01-15",
        });

        var settled = await (await client.PostAsJsonAsync(Url("payments"), new
        {
            title, amount = "100", date = "2026-01-14", time = "10:30", status = "Paid", due_date = "2026-01-15", note = "",
        })).JsonAsync();

        Assert.True(settled.GetProperty("settled").GetBoolean());
        var summary = settled.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("payment_count").GetInt32());
        Assert.Equal(1, summary.GetProperty("paid_count").GetInt32());
        Assert.Equal("2026-02-15", summary.GetProperty("next_due_date").GetString());
        var first = summary.GetProperty("payments").EnumerateArray()
            .Single(p => p.GetProperty("due_date").GetString() == "2026-01-15");
        Assert.Equal("Paid", first.GetProperty("status").GetString());
        Assert.Equal("2026-01-14", first.GetProperty("date").GetString());
        Assert.Equal("Instalment 1 of 3", first.GetProperty("note").GetString());

        // No Unpaid instalment on that day: it is an extra payment, so it gets its own row.
        var extra = await (await client.PostAsJsonAsync(Url("payments"), new
        {
            title, amount = "50", date = "2026-01-20", time = "10:30", status = "Paid", due_date = "2026-01-20", note = "Extra",
        })).JsonAsync();
        Assert.False(extra.GetProperty("settled").GetBoolean());
        Assert.Equal(4, extra.GetProperty("summary").GetProperty("payment_count").GetInt32());

        await client.DeleteAsync(Url($"accounts/{Uri.EscapeDataString(title)}"));
    }

    [Fact]
    public async Task Validation_errors_come_back_as_problem_details()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Url("accounts/preview"), new { total_amount = "abc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrEmpty(await response.DetailAsync()));
    }

    [Fact]
    public async Task Budget_lines_can_be_added_copied_and_deleted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var added = await client.PostAsJsonAsync(Url("budget/lines"), new
        {
            section = "Expense", group = "Home", item = "Rent", year = 2031, amounts = new object[] { 100, "200" },
        });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var duplicate = await client.PostAsJsonAsync(Url("budget/lines"), new { section = "Expense", group = "Home", item = "rent", year = 2031 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var copied = await (await client.PostAsJsonAsync(Url("budget/copy-year"), new { from = 2031, to = 2032, include_amounts = true })).JsonAsync();
        Assert.Equal(1, copied.GetProperty("copied").GetInt32());

        foreach (var year in new[] { 2031, 2032 })
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url($"budget/years/{year}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url("budget/years/2031"))).StatusCode);
    }

    [Fact]
    public async Task Import_previews_first_then_applies()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var sample = await client.GetByteArrayAsync(Url("import/sample/budget"));

        var preview = await (await client.PostAsync(Url("import/budget"), Form(sample, dryRun: null))).JsonAsync();
        Assert.False(preview.GetProperty("applied").GetBoolean());
        var adds = preview.GetProperty("counts").GetProperty("add").GetInt32();
        Assert.True(adds > 0);

        var applied = await (await client.PostAsync(Url("import/budget"), Form(sample, dryRun: ""))).JsonAsync();
        Assert.True(applied.GetProperty("applied").GetBoolean());

        var again = await (await client.PostAsync(Url("import/budget"), Form(sample, dryRun: "1"))).JsonAsync();
        Assert.Equal(adds, again.GetProperty("counts").GetProperty("skip").GetInt32());
    }

    [Fact]
    public async Task Import_rejects_a_missing_file_and_unknown_samples()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var noFile = await client.PostAsync(Url("import/payments"), new MultipartFormDataContent { { new StringContent("1"), "dry_run" } });
        Assert.Equal("Choose a .csv or .xlsx file to import.", await noFile.DetailAsync());

        var sample = await client.GetAsync(Url("import/sample/nope"));
        Assert.Equal(HttpStatusCode.NotFound, sample.StatusCode);
    }

    [Fact]
    public async Task The_workbook_downloads_as_xlsx()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Url("download"));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("payments.xlsx", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("PK"u8.ToArray(), bytes[..2]);
    }

    [Fact]
    public async Task Numbers_and_text_are_both_accepted_for_amounts()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Loose " + Guid.NewGuid().ToString("N")[..6];
        await client.PostAsJsonAsync(Url("accounts"), new { title, total_amount = "1,000" });

        var asNumber = await client.PostAsJsonAsync(Url("payments"), new { title, amount = 12.5 });
        var asText = await client.PostAsJsonAsync(Url("payments"), new { title, amount = "1,234.50" });

        Assert.Equal(HttpStatusCode.Created, asNumber.StatusCode);
        var summary = (await asText.JsonAsync()).GetProperty("summary");
        Assert.Equal(1247, summary.GetProperty("total_paid").GetDouble());
    }

    [Fact]
    public async Task Paid_payments_flow_into_the_budget_and_back_out()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Sync " + Guid.NewGuid().ToString("N")[..6];
        await client.PostAsJsonAsync(Url("accounts"), new { title, total_amount = 1000 });

        await client.PostAsJsonAsync(Url("payments"), new { title, amount = 100, date = "2033-03-10", status = "Paid" });
        var second = await (await client.PostAsJsonAsync(Url("payments"), new { title, amount = 50, date = "2033-03-20", status = "Paid" })).JsonAsync();
        var line = await BudgetLineAsync(client, 2033, title);
        Assert.Equal("Loans", line.Group);
        Assert.Equal(150, line.Amounts[2]);

        var id = second.GetProperty("id").GetString();
        await client.PatchAsJsonAsync(Url($"payments/{id}"), new { status = "Unpaid", title });
        Assert.Equal(100, (await BudgetLineAsync(client, 2033, title)).Amounts[2]);

        await client.DeleteAsync(Url($"accounts/{Uri.EscapeDataString(title)}"));
        Assert.Equal(0, (await BudgetLineAsync(client, 2033, title)).Amounts[2]);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(Url("budget/years/2033"))).StatusCode);
    }

    [Fact]
    public async Task Today_lists_soon_due_payments_and_the_month_budget()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var title = "Today " + Guid.NewGuid().ToString("N")[..6];
        var due = DateOnly.FromDateTime(DateTime.Today.AddDays(2)).ToString("yyyy-MM-dd");
        await client.PostAsJsonAsync(Url("accounts"), new { title, total_amount = 300 });
        await client.PostAsJsonAsync(Url("payments"), new { title, amount = 75, date = due, status = "Unpaid", due_date = due });

        var response = await client.GetAsync(new Uri("/api/today", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var today = await response.JsonAsync();

        var payment = today.GetProperty("payments").EnumerateArray().Single(p => p.GetProperty("title").GetString() == title);
        Assert.Equal(75, payment.GetProperty("amount").GetDouble());
        Assert.True(today.GetProperty("budget").TryGetProperty("left", out _));
        Assert.Equal(JsonValueKind.Array, today.GetProperty("reminders").ValueKind);
        Assert.Equal(JsonValueKind.Array, today.GetProperty("habits").ValueKind);

        await client.DeleteAsync(Url($"accounts/{Uri.EscapeDataString(title)}"));
    }

    private static async Task<(string Group, double[] Amounts)> BudgetLineAsync(HttpClient client, int year, string item)
    {
        var budget = await (await client.GetAsync(Url($"budget?year={year}"))).JsonAsync();
        var line = budget.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("groups").EnumerateArray())
            .SelectMany(g => g.GetProperty("lines").EnumerateArray())
            .Single(l => l.GetProperty("item").GetString() == item);
        return (line.GetProperty("group").GetString()!, [.. line.GetProperty("amounts").EnumerateArray().Select(a => a.GetDouble())]);
    }

    private static MultipartFormDataContent Form(byte[] file, string? dryRun)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", "budget.csv" } };
        if (dryRun is not null) form.Add(new StringContent(dryRun, Encoding.UTF8), "dry_run");
        return form;
    }
}