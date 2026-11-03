using Rose.Domain.Common;
using Rose.Domain.Savings;

namespace Rose.UnitTests.Domain;

public class SavingsRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static SavingsEntry Entry(int daysAgo, decimal amount) =>
        new(Guid.NewGuid().ToString(), Today.AddDays(-daysAgo), amount, "");

    private static SavingsGoal Goal(decimal target, DateOnly? deadline, params SavingsEntry[] entries) => new()
    {
        Id = "g1",
        Name = "Laptop",
        Target = target,
        Deadline = deadline,
        Entries = [.. entries],
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Theory]
    [InlineData("2026-12-31", 30000, 15000)]     // 59 days -> 2 months
    [InlineData("2026-11-03", 1000, 1000)]       // due today -> everything now
    [InlineData("2027-11-02", 1000, 83.34)]      // 365 days -> 12 months, rounded up to the cent
    public void MonthlyNeeded_spreads_the_rest_over_the_months_left(string deadline, decimal remaining, decimal expected) =>
        Assert.Equal(expected, SavingsRules.MonthlyNeeded(remaining, DateOnly.Parse(deadline), Today));

    [Fact]
    public void MonthlyNeeded_is_null_without_a_future_deadline_or_when_reached()
    {
        Assert.Null(SavingsRules.MonthlyNeeded(100, null, Today));
        Assert.Null(SavingsRules.MonthlyNeeded(100, Today.AddDays(-1), Today));
        Assert.Null(SavingsRules.MonthlyNeeded(0, Today.AddDays(30), Today));
    }

    [Fact]
    public void DailyPace_uses_at_least_thirty_days()
    {
        // One 3,000 deposit yesterday is 100/day, not 1,500/day.
        Assert.Equal(100m, SavingsRules.DailyPace([Entry(1, 3000)], Today));
    }

    [Fact]
    public void DailyPace_ignores_entries_older_than_ninety_days_and_counts_withdrawals()
    {
        var pace = SavingsRules.DailyPace([Entry(200, 50_000), Entry(89, 1000), Entry(10, -100)], Today);

        Assert.Equal(10m, pace); // 900 over 90 days
    }

    [Fact]
    public void EstimatedFinish_follows_the_pace()
    {
        Assert.Equal(Today.AddDays(10), SavingsRules.EstimatedFinish(1000, 100, Today));
        Assert.Null(SavingsRules.EstimatedFinish(1000, 0, Today));
        Assert.Null(SavingsRules.EstimatedFinish(0, 100, Today));
    }

    [Fact]
    public void Summarize_reports_progress_and_status()
    {
        var onTrack = SavingsRules.Summarize(Goal(10_000, Today.AddDays(200), Entry(29, 3000)), Today);
        Assert.Equal(3000m, onTrack.Saved);
        Assert.Equal(7000m, onTrack.Remaining);
        Assert.Equal(30, onTrack.Percent);
        Assert.Equal(Today.AddDays(70), onTrack.EstimatedFinish); // 3,000 over 30 days (incl. today) = 100/day
        Assert.Equal(GoalStatus.OnTrack, onTrack.Status);

        Assert.Equal(GoalStatus.Behind, SavingsRules.Summarize(Goal(10_000, Today.AddDays(20), Entry(30, 3000)), Today).Status);
        Assert.Equal(GoalStatus.Overdue, SavingsRules.Summarize(Goal(10_000, Today.AddDays(-1)), Today).Status);
        Assert.Equal(GoalStatus.NoDeadline, SavingsRules.Summarize(Goal(10_000, null), Today).Status);

        var done = SavingsRules.Summarize(Goal(1000, null, Entry(1, 1500)), Today);
        Assert.Equal(GoalStatus.Done, done.Status);
        Assert.Equal(0m, done.Remaining);
        Assert.Equal(100, done.Percent);
    }

    [Fact]
    public void Summarize_lists_entries_newest_first()
    {
        var older = Entry(5, 10);
        var newer = Entry(1, 20);
        var sameDayLater = Entry(1, 30);

        var view = SavingsRules.Summarize(Goal(1000, null, older, newer, sameDayLater), Today);

        Assert.Equal([sameDayLater.Id, newer.Id, older.Id], view.Entries.Select(e => e.Id));
    }

    [Theory]
    [InlineData("", 100, null, "Give the goal a name.")]
    [InlineData("Car", 0, null, "The target must be more than zero.")]
    [InlineData("Car", 100, "31/12/2026", "Pick a valid deadline.")]
    public void ParseGoal_rejects_bad_input(string name, double target, string? deadline, string message)
    {
        var error = Assert.Throws<ValidationException>(() => SavingsRules.ParseGoal(name, null, (decimal)target, deadline));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ParseEntry_signs_withdrawals_and_defaults_to_today()
    {
        var entry = SavingsRules.ParseEntry("withdraw", 25.555m, null, " cash ", saved: 100, Today);

        Assert.Equal(new EntryInput(Today, -25.56m, "cash"), entry);
    }

    [Fact]
    public void ParseEntry_refuses_to_withdraw_more_than_saved_or_to_date_in_the_future()
    {
        Assert.Equal("You can withdraw at most 100.",
            Assert.Throws<ValidationException>(() => SavingsRules.ParseEntry("withdraw", 101, null, null, 100, Today)).Message);
        Assert.Equal("The date cannot be in the future.",
            Assert.Throws<ValidationException>(() => SavingsRules.ParseEntry("deposit", 1, "2026-11-04", null, 0, Today)).Message);
    }
}