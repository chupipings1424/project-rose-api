using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.UnitTests.Domain;

public class ScheduleTests
{
    private static readonly DateOnly Today = new(2026, 1, 10);

    [Theory]
    [InlineData("Monthly", 1, "2024-02-29")]
    [InlineData("Monthly", 2, "2024-03-31")]
    [InlineData("Quarterly", 1, "2024-04-30")]
    [InlineData("Weekly", 1, "2024-02-07")]
    [InlineData("Bi-weekly", 1, "2024-02-14")]
    [InlineData("fortnightly", 1, "2024-02-14")]
    [InlineData("Yearly", 1, "2025-01-31")]
    public void NextDue_steps_from_the_start_and_clamps_month_ends(string frequency, int n, string expected) =>
        Assert.Equal(expected, Coerce.IsoDate(Schedule.NextDue(new DateOnly(2024, 1, 31), frequency, n)));

    [Fact]
    public void NextDue_rejects_unknown_frequencies()
    {
        var error = Assert.Throws<ValidationException>(() => Schedule.NextDue(Today, "hourly"));
        Assert.Contains("Unknown frequency 'hourly'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitTotal_adds_back_up_exactly()
    {
        var parts = Schedule.SplitTotal(100, 3);
        Assert.Equal([33.33, 33.33, 33.34], parts);
        Assert.Equal(10000, parts.Sum(p => (long)Math.Round(p * 100)));
    }

    [Fact]
    public void Build_with_total_mode_splits_the_total()
    {
        var plan = Schedule.Build("1,200", "6", "monthly", "2026-01-31", null, Today);

        Assert.Equal("Monthly", plan.Frequency);
        Assert.Equal(200, plan.PerPayment);
        Assert.Equal(1200, plan.Total);
        Assert.Equal("2026-01-31", plan.FirstDue);
        Assert.Equal("2026-02-28", plan.Dates[1]);
        Assert.Equal("2026-06-30", plan.FinalDue);
    }

    [Fact]
    public void Build_with_per_payment_mode_multiplies()
    {
        var plan = Schedule.Build("99.99", "3", "Weekly", null, "per_payment", Today);

        Assert.Equal(299.97, plan.Total);
        Assert.Equal("2026-01-10", plan.FirstDue);
    }

    [Theory]
    [InlineData("100", "0", "Number of payments must be at least 1.")]
    [InlineData("0", "3", "Amount must be greater than zero.")]
    public void Build_validates_input(string amount, string count, string message) =>
        Assert.Equal(message, Assert.Throws<ValidationException>(
            () => Schedule.Build(amount, count, "Monthly", null, null, Today)).Message);
}