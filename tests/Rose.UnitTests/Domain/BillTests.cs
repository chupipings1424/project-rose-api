using Rose.Application.Tracker;
using Rose.Domain.Common;
using Rose.Domain.Reporting;
using Rose.Domain.Tracker;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Domain;

/// <summary>Bills: open-ended monthly titles (water, WiFi) with no total and no number of payments.</summary>
public sealed class BillTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static Account Bill(string title) => new(title, 0, "", "", "Monthly", 0, 0, "") { Kind = AccountKind.Bill };

    private static Payment Paid(string title, double amount, string date) =>
        new(Guid.NewGuid().ToString(), title, amount, date, "9:00am", PaymentStatus.Paid, "", "");

    [Theory]
    [InlineData("bill", "bill")]
    [InlineData(" BILL ", "bill")]
    [InlineData("plan", "plan")]
    [InlineData("", "plan")]
    [InlineData(null, "plan")]
    [InlineData("other", "plan")]
    public void Kind_is_normalised(string? raw, string expected) => Assert.Equal(expected, AccountKind.Normalize(raw));

    [Fact]
    public void A_bill_summary_shows_monthly_totals_instead_of_a_plan()
    {
        Payment[] payments =
        [
            Paid("Water", 300, "2025-10-20"), // 13 months ago: outside the chart
            Paid("Water", 400, "2026-01-10"),
            Paid("Water", 500, "2026-09-12"),
            Paid("water", 250, "2026-11-02"),
            Paid("water", 50, "2026-11-03"),
            Paid("WiFi", 1500, "2026-11-01"),
        ];

        var summary = AccountSummary.For("Water", [Bill("Water")], payments, Today);

        Assert.Equal(AccountKind.Bill, summary.Kind);
        Assert.False(summary.HasPlan);
        Assert.Equal(300, summary.PaidThisMonth);
        Assert.Equal(1200, summary.PaidThisYear);
        Assert.Equal(400, summary.AveragePerMonth); // (400 + 500 + 300) / 3 months with payments
        Assert.Equal(1500, summary.TotalAmount);
        Assert.Equal(0, summary.Remaining);
        Assert.Equal(0, summary.Progress);
        Assert.False(summary.Overdue);
        Assert.Equal(AccountSummary.MonthsShown, summary.Monthly.Count);
        Assert.Equal(new MonthTotal("2025-12", 0), summary.Monthly[0]);
        Assert.Equal(new MonthTotal("2026-01", 400), summary.Monthly[1]);
        Assert.Equal(new MonthTotal("2026-11", 300), summary.Monthly[^1]);
    }

    [Fact]
    public void A_bill_with_nothing_paid_yet_is_all_zeros()
    {
        var summary = AccountSummary.For("Water", [Bill("Water")], [], Today);

        Assert.Equal(0, summary.PaidThisMonth);
        Assert.Equal(0, summary.AveragePerMonth);
        Assert.All(summary.Monthly, m => Assert.Equal(0, m.Amount));
    }

    [Fact]
    public void Plans_still_get_their_progress()
    {
        var plan = new Account("Car", 1000, "", "", "Monthly", 4, 250, "");
        var summary = AccountSummary.For("Car", [plan], [Paid("Car", 250, "2026-11-01")], Today);

        Assert.Equal(AccountKind.Plan, summary.Kind);
        Assert.True(summary.HasPlan);
        Assert.Equal(750, summary.Remaining);
        Assert.Equal(25, summary.Progress);
        Assert.Equal(250, summary.PaidThisMonth);
    }

    [Fact]
    public void The_report_keeps_bills_out_of_the_plan_progress()
    {
        Account[] accounts = [new("Car", 1000, "", "", "Monthly", 4, 250, ""), Bill("Water")];
        Payment[] payments = [Paid("Car", 250, "2026-11-01"), Paid("Water", 480, "2026-11-02")];

        var report = ReportBuilder.Build(accounts, payments, [], "2026", new DateTime(2026, 11, 5, 9, 0, 0), "");

        Assert.Equal(1000, report.Kpis.TrackedTotal);
        Assert.Equal(250, report.Kpis.PaidTotal);
        Assert.Equal(25, report.Kpis.Progress);
        Assert.DoesNotContain(report.Payments.Timeline, t => t.Title == "Water");
        Assert.Equal(730, report.Payments.Activity[10].Paid);
        var water = report.Payments.ByTitle.Single(t => t.Title == "Water");
        Assert.Equal((AccountKind.Bill, 480d, 0d, 0d), (water.Kind, water.Paid, water.Remaining, water.Progress));
        Assert.Equal(AccountKind.Plan, report.Payments.ByTitle.Single(t => t.Title == "Car").Kind);
    }

    [Fact]
    public async Task Creating_a_bill_needs_only_a_title()
    {
        using var book = new MemoryTrackerBook();
        var service = new PaymentTrackerService(
            new MemoryTrackerStore(book), new FixedCurrentUser(TestUsers.Alice),
            new FakeClock(new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero)));

        var accounts = await service.CreateAccountAsync(
            new CreateAccountRequest("Water", null, null, null, null, null, null, null, "Maynilad", Kind: "bill"));

        var water = Assert.Single(accounts);
        Assert.True(water.IsBill);
        Assert.Equal("Maynilad", water.Notes);
        Assert.Empty(book.Payments());
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAccountAsync(
            new CreateAccountRequest("WiFi", null, null, null, null, null, null, null, null, Kind: "loan")));
    }
}