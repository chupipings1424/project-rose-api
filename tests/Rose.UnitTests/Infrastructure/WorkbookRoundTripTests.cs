using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using Rose.Application.Tracker;
using Rose.Domain.Common;
using Rose.Infrastructure;
using Rose.Infrastructure.Excel;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Infrastructure;

/// <summary>Real payments.xlsx files in a temp folder: what goes in must come back out.</summary>
public sealed class WorkbookRoundTripTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly ClosedXmlTrackerWorkbookStore _store;
    private readonly PaymentTrackerService _tracker;
    private readonly BudgetService _budget;

    public WorkbookRoundTripTests()
    {
        _store = new ClosedXmlTrackerWorkbookStore(Options.Create(new RoseOptions { DataDirectory = _dir.Path }), _clock);
        _tracker = new PaymentTrackerService(_store, _clock);
        _budget = new BudgetService(_store, _clock);
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
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
    public async Task Payments_survive_reopening_and_can_be_deleted()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "100", null, "Monthly", null, null, null, null));
        var created = await _tracker.AddPaymentAsync(new AddPaymentRequest("Rent", "1,234.50", "2026-03-01", "13:05", null, null, "march"));

        using (var fresh = new ClosedXmlTrackerWorkbookStore(Options.Create(new RoseOptions { DataDirectory = _dir.Path }), _clock))
        {
            var payment = Assert.Single((await new PaymentTrackerService(fresh, _clock).SummaryAsync("Rent")).Payments);
            Assert.Equal(1234.5, payment.Amount);
            Assert.Equal("1:05pm", payment.Time);
            Assert.Equal("Paid", payment.Status);
        }

        await _tracker.DeletePaymentAsync(created.Id);
        Assert.Empty((await _tracker.SummaryAsync("Rent")).Payments);
    }

    [Fact]
    public async Task Budget_changes_rebuild_the_year_report_sheet()
    {
        var line = await _budget.AddLineAsync(new AddBudgetLineRequest(
            "Expenses", "Home", "Rent", "2026", ["100", "200"], "flat"));
        await _budget.UpdateLineAsync(line.Id, new UpdateBudgetLineRequest("Mar", "42.5", null, null, null, null, null));
        await _budget.CopyYearAsync(new CopyBudgetYearRequest("2026", "2027", IncludeAmounts: true));

        using var workbook = new XLWorkbook(Path.Combine(_dir.Path, "payments.xlsx"));
        Assert.True(workbook.Worksheets.Contains("Budget 2026"));
        Assert.True(workbook.Worksheets.Contains("Budget 2027"));

        var view = await _budget.GetAsync("2027");
        var copied = Assert.Single(view.Section("Expense").Groups.SelectMany(g => g.Lines));
        Assert.Equal([100, 200, 42.5, 0, 0, 0, 0, 0, 0, 0, 0, 0], copied.Amounts);
    }

    [Fact]
    public async Task Deleting_the_last_line_of_a_year_removes_its_sheet()
    {
        await _budget.AddLineAsync(new AddBudgetLineRequest("Revenue", "Income", "Salary", "2025", null, null));

        await _budget.DeleteYearAsync(2025);

        using var workbook = new XLWorkbook(Path.Combine(_dir.Path, "payments.xlsx"));
        Assert.False(workbook.Worksheets.Contains("Budget 2025"));
    }

    [Fact]
    public async Task Concurrent_writes_are_serialised()
    {
        await _tracker.CreateAccountAsync(new CreateAccountRequest("Rent", null, "100", null, "Monthly", null, null, null, null));

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i =>
            _tracker.AddPaymentAsync(new AddPaymentRequest("Rent", i.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, null, null, null))));

        Assert.Equal(20, (await _tracker.SummaryAsync("Rent")).Payments.Count);
    }
}