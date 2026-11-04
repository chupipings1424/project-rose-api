using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rose.Application.Abstractions;
using Rose.Application.Tracker;
using Rose.Domain.Common;
using Rose.Infrastructure.Persistence;

namespace Rose.Api.IntegrationTests;

/// <summary>
/// The payment tracker and budget against a real PostgreSQL database: what goes in must come back
/// out. Each test gets its own empty database; everything is saved as one user.
/// </summary>
public sealed class TrackerStoreTests : IDisposable
{
    private readonly RoseApiFactory _factory = new();
    private static readonly FixedUser Me = new FixedUser(RoseApiFactory.UserIdFor(RoseApiFactory.AdminEmail));
    private readonly ITrackerWorkbookStore _store;
    private readonly PaymentTrackerService _tracker;
    private readonly BudgetService _budget;

    public TrackerStoreTests()
    {
        _store = _factory.Services.GetRequiredService<ITrackerWorkbookStore>();
        _tracker = new PaymentTrackerService(_store, Me, TimeProvider.System);
        _budget = new BudgetService(_store, Me, TimeProvider.System);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private XLWorkbook Export()
    {
        var bytes = _store.ExportAsync(Me.Id).GetAwaiter().GetResult();
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public async Task A_title_with_instalments_creates_its_payments()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest(
            "Car/Loan", null, "1000", "4", "Monthly", "2026-01-31", null, null, "note"));

        var summary = await _tracker.SummaryAsync("Car/Loan");

        Assert.Equal(4, summary.Payments.Count);
        Assert.Equal(1000, summary.Payments.Sum(p => p.Amount));
        Assert.Equal("2026-02-28", summary.Payments[1].DueDate);
    }

    [Fact]
    public async Task Duplicate_titles_conflict_case_insensitively()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "10", null, "Monthly", null, null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _tracker.CreateAccountAsync(new CreateAccountRequest("rent", null, "10", null, "Monthly", null, null, null, null)));
    }

    [Fact]
    public async Task Payments_survive_a_fresh_store_and_can_be_deleted()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "100", null, "Monthly", null, null, null, null));
        var created = await _tracker.AddPaymentAsync(new AddPaymentRequest("Rent", "1,234.50", "2026-03-01", "13:05", null, null, "march"));

        using (var fresh = new PostgresTrackerStore(
            _factory.Services.GetRequiredService<IDbContextFactory<RoseDbContext>>(), TimeProvider.System))
        {
            var payment = Assert.Single((await new PaymentTrackerService(fresh, Me, TimeProvider.System).SummaryAsync("Rent")).Payments);
            Assert.Equal(1234.5, payment.Amount);
            Assert.Equal("1:05pm", payment.Time);
            Assert.Equal("Paid", payment.Status);
            Assert.Equal("2026-03-01", payment.Date);
        }

        await _tracker.DeletePaymentAsync(created.Id);
        Assert.Empty((await _tracker.SummaryAsync("Rent")).Payments);
    }

    [Fact]
    public async Task Deleting_a_title_removes_its_payments()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Phone", null, "300", "3", "Monthly", "2026-01-15", null, null, null));

        await _tracker.DeleteAccountAsync("phone");

        await using var db = _factory.Database();
        Assert.Empty(await db.Accounts.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Budget_changes_show_up_in_the_excel_export()
    {
        var line = await _budget.AddLineAsync(new AddBudgetLineRequest(
            "Expenses", "Home", "Rent", "2026", ["100", "200"], "flat"));
        await _budget.UpdateLineAsync(line.Id, new UpdateBudgetLineRequest("Mar", "42.5", null, null, null, null, null));
        await _budget.CopyYearAsync(new CopyBudgetYearRequest("2026", "2027", IncludeAmounts: true));

        using (var workbook = Export())
        {
            Assert.True(workbook.Worksheets.Contains("Budget 2026"));
            Assert.True(workbook.Worksheets.Contains("Budget 2027"));
        }

        var view = await _budget.GetAsync("2027");
        var copied = Assert.Single(view.Section("Expense").Groups.SelectMany(g => g.Lines));
        Assert.Equal([100, 200, 42.5, 0, 0, 0, 0, 0, 0, 0, 0, 0], copied.Amounts);
    }

    [Fact]
    public async Task Deleting_the_last_line_of_a_year_removes_it_from_the_export()
    {
        await _budget.AddLineAsync(new AddBudgetLineRequest("Revenue", "Income", "Salary", "2025", null, null));

        await _budget.DeleteYearAsync(2025);

        using var workbook = Export();
        Assert.False(workbook.Worksheets.Contains("Budget 2025"));
    }

    [Fact]
    public async Task Concurrent_writes_are_serialised()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "100", null, "Monthly", null, null, null, null));

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i =>
            _tracker.AddPaymentAsync(new AddPaymentRequest("Rent", i.ToString(CultureInfo.InvariantCulture), null, null, null, null, null))));

        Assert.Equal(20, (await _tracker.SummaryAsync("Rent")).Payments.Count);
    }

    [Fact]
    public async Task Another_user_has_an_empty_tracker_of_their_own()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "100", "2", "Monthly", "2026-01-31", null, null, null));
        var bob = new FixedUser(Guid.NewGuid());
        var theirs = new PaymentTrackerService(_store, bob, TimeProvider.System);

        Assert.Empty(await theirs.AccountsAsync());
        await theirs.CreateAccountAsync(new CreateAccountRequest("Rent", null, "5", null, "Monthly", null, null, null, null));

        Assert.Single(await theirs.AccountsAsync());
        Assert.Equal(2, (await _tracker.SummaryAsync("Rent")).Payments.Count);
        using var workbook = new XLWorkbook(new MemoryStream(await _store.ExportAsync(bob.Id)));
        Assert.Equal(2, workbook.Worksheet("Accounts").RowsUsed().Count()); // header + Bob's one title
    }

    private sealed record FixedUser(Guid Id) : ICurrentUser;
}