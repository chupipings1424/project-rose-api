using ClosedXML.Excel;
using Rose.Domain.Loans;
using Rose.Infrastructure.Excel;

namespace Rose.UnitTests.Infrastructure;

public sealed class LoanReportWriterTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static LoanView View(string name, string loanDate = "2026-04-02") => LoanRules.Summarize(new Loan
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Type = "Coop",
        LoanDate = DateOnly.Parse(loanDate),
        Term = 24,
        MonthlyAmortization = 500,
        Amount = 10_000,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    }, Today);

    [Fact]
    public void Writes_a_summary_sheet_and_one_schedule_per_loan()
    {
        var bytes = new LoanReportWriter().Write([View("Coop"), View("SSS: salary/loan")], Today);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Loans", "Coop", "SSS  salary loan"], workbook.Worksheets.Select(ws => ws.Name));

        var summary = workbook.Worksheet("Loans");
        Assert.Equal("Coop", summary.Cell(4, 1).GetString());
        Assert.Equal(8_500, summary.Cell(4, 9).GetDouble());
        Assert.Equal("7 of 24", summary.Cell(4, 10).GetString());
        Assert.Equal("TOTAL", summary.Cell(6, 1).GetString());
        Assert.Equal(17_000, summary.Cell(6, 9).GetDouble());

        var schedule = workbook.Worksheet("Coop");
        var head = schedule.Column(1).CellsUsed().First(c => c.GetString() == "#").Address.RowNumber;
        Assert.Equal(new DateTime(2026, 5, 2), schedule.Cell(head + 1, 2).GetDateTime());
        Assert.Equal("Paid", schedule.Cell(head + 7, 5).GetString());
        Assert.Equal("Upcoming", schedule.Cell(head + 8, 5).GetString());
        Assert.Equal(0, schedule.Cell(head + 24, 4).GetDouble());
    }

    [Fact]
    public void An_empty_report_still_opens()
    {
        using var workbook = new XLWorkbook(new MemoryStream(new LoanReportWriter().Write([], Today)));
        Assert.Equal("Loans", Assert.Single(workbook.Worksheets).Name);
    }

    [Fact]
    public void Sheet_names_are_short_safe_and_unique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Loans" };

        Assert.Equal("loans (2)", LoanReportWriter.SheetName("loans", used));
        Assert.Equal("A very long loan name that keep", LoanReportWriter.SheetName("A very long loan name that keeps going", used));
        Assert.Equal("A very long loan name that (2)", LoanReportWriter.SheetName("A very long loan name that keeps going", used));
        Assert.Equal("Loan", LoanReportWriter.SheetName("[]", used));
    }
}