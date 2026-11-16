using ClosedXML.Excel;
using Rose.Domain.Budget;
using Rose.Domain.Tracker;
using Rose.Infrastructure.Excel;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Infrastructure;

/// <summary>The download: what is exported to payments.xlsx must read back the same, in the classic layout.</summary>
public sealed class ExcelExportTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Export_round_trips_titles_payments_and_budget()
    {
        using var source = ClosedXmlTrackerWorkbook.CreateEmpty(_clock);
        source.AddAccount(new Account("Car Loan", 1000, "2026-01-31", "2026-01-31", "Monthly", 4, 250, "note"));
        source.AddAccount(new Account("Water", 0, "", "", "Monthly", 0, 0, "") { Kind = AccountKind.Bill });
        source.AddPayment(new Payment("p2", "Car Loan", 250, "2026-02-28", "9:00am", "Unpaid", "2026-02-28", "Instalment 2 of 4"));
        source.AddPayment(new Payment("p1", "Car Loan", 1234.5, "2026-01-31", "1:05pm", "Paid", "2026-01-31", ""));
        source.AddBudgetLine(new BudgetLine("b1", 2026, "Expense", "Home", "Rent", [100, 200, 42.5, 0, 0, 0, 0, 0, 0, 0, 0, 0], 342.5, "flat"));
        source.AddBudgetLine(new BudgetLine("b2", 2027, "Revenue", "Income", "Salary", new double[12], 0, ""));

        var bytes = ExcelExport.Build(source, _clock);

        using var excel = new XLWorkbook(new MemoryStream(bytes));
        Assert.True(excel.Worksheets.Contains("Budget 2026"));
        Assert.True(excel.Worksheets.Contains("Budget 2027"));

        using var back = new ClosedXmlTrackerWorkbook(new XLWorkbook(new MemoryStream(bytes)), _clock);
        Assert.Equal(source.Accounts(), back.Accounts());
        Assert.Equal(["p1", "p2"], back.Payments().Select(p => p.Id));
        Assert.Equal(1234.5, back.Payments()[0].Amount);
        Assert.Equal("1:05pm", back.Payments()[0].Time);
        var rent = back.BudgetLines()[0];
        Assert.Equal([100, 200, 42.5, 0, 0, 0, 0, 0, 0, 0, 0, 0], rent.Amounts);
        Assert.Equal(342.5, rent.Total);
    }

    [Fact]
    public void An_empty_tracker_exports_the_two_base_sheets()
    {
        using var source = ClosedXmlTrackerWorkbook.CreateEmpty(_clock);

        using var excel = new XLWorkbook(new MemoryStream(ExcelExport.Build(source, _clock)));

        Assert.Equal(["Accounts", "Payments"], excel.Worksheets.Select(w => w.Name));
    }
}