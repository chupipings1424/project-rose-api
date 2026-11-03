using Rose.Domain.Common;
using Rose.Domain.Habits;

namespace Rose.UnitTests.Domain;

public class HabitRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);

    private static Habit NewHabit(string kind = HabitKind.Check, int target = 1, TimeOnly? remindAt = null) => new()
    {
        Id = "h1",
        Name = "Read",
        Kind = kind,
        Target = target,
        RemindAt = remindAt,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Marks the given days (counted back from <see cref="Today"/>) with <paramref name="value"/>.</summary>
    private static Habit With(Habit habit, int value, params int[] daysAgo)
    {
        foreach (var ago in daysAgo) habit.Log[Today.AddDays(-ago)] = value;
        return habit;
    }

    private static DateTimeOffset LocalTime(int hour, int minute = 0, int dayOffset = 0) =>
        new DateTimeOffset(Today.Year, Today.Month, Today.Day, hour, minute, 0, Cet).AddDays(dayOffset);

    [Fact]
    public void Streak_counts_back_from_today_when_today_is_done() =>
        Assert.Equal(3, HabitRules.CurrentStreak(With(NewHabit(), 1, 0, 1, 2, 4), Today));

    [Fact]
    public void Streak_stays_alive_until_today_is_over() =>
        Assert.Equal(2, HabitRules.CurrentStreak(With(NewHabit(), 1, 1, 2), Today));

    [Fact]
    public void Streak_breaks_after_a_missed_day() =>
        Assert.Equal(0, HabitRules.CurrentStreak(With(NewHabit(), 1, 2, 3), Today));

    [Fact]
    public void Count_habits_only_count_days_that_reach_the_target()
    {
        var habit = NewHabit(HabitKind.Count, 8);
        With(habit, 8, 0, 1);
        With(habit, 5, 2);
        With(habit, 9, 3);

        Assert.Equal(2, HabitRules.CurrentStreak(habit, Today));
        Assert.Equal(2, HabitRules.BestStreak(habit));
        Assert.True(HabitRules.IsComplete(habit, Today.AddDays(-3)));
    }

    [Fact]
    public void Best_streak_finds_the_longest_run() =>
        Assert.Equal(4, HabitRules.BestStreak(With(NewHabit(), 1, 0, 1, 5, 6, 7, 8, 20)));

    [Fact]
    public void Summary_only_returns_the_recent_log()
    {
        var view = HabitRules.Summarize(With(NewHabit(), 1, 0, 90, 91, 200), Today);

        Assert.Equal([Today.AddDays(-90), Today], view.Log.Keys);
        Assert.True(view.DoneToday);
        Assert.Equal(1, view.Today);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void The_last_seven_days_can_be_changed(int daysAgo) =>
        Assert.Equal(Today.AddDays(-daysAgo), HabitRules.ParseDay(Today.AddDays(-daysAgo).ToString("yyyy-MM-dd"), Today));

    [Theory]
    [InlineData("2026-11-11", "hasn't happened yet")]
    [InlineData("2026-11-03", "last 7 days")]
    [InlineData("11/10/2026", "valid date")]
    [InlineData(null, "valid date")]
    public void Other_days_are_refused(string? date, string message) =>
        Assert.Contains(message, Assert.Throws<ValidationException>(() => HabitRules.ParseDay(date, Today)).Message);

    [Theory]
    [InlineData(HabitKind.Check, 2)]
    [InlineData(HabitKind.Check, -1)]
    [InlineData(HabitKind.Count, 10_000)]
    public void Values_outside_the_range_are_refused(string kind, int value) =>
        Assert.Throws<ValidationException>(() => HabitRules.ParseValue(NewHabit(kind, 3), value));

    [Fact]
    public void Setting_zero_removes_the_day()
    {
        var habit = With(NewHabit(), 1, 0);
        HabitRules.SetDay(habit, Today, 0);
        Assert.Empty(habit.Log);
    }

    [Fact]
    public void Parse_cleans_a_check_habit()
    {
        var input = HabitRules.ParseHabit("  Stretch ", null, null, 50, "ignored", "07:30");

        Assert.Equal(new HabitInput("Stretch", "", HabitKind.Check, 1, "", new TimeOnly(7, 30)), input);
    }

    [Fact]
    public void Parse_keeps_target_and_unit_for_count_habits() =>
        Assert.Equal(
            new HabitInput("Water", "", HabitKind.Count, 8, "glasses", null),
            HabitRules.ParseHabit("Water", "", "count", 8, " glasses ", ""));

    [Theory]
    [InlineData("", "check", 1, null, "name")]
    [InlineData("Water", "count", 0, null, "target")]
    [InlineData("Water", "weekly", 1, null, "check or a count")]
    [InlineData("Water", "check", 1, "25:00", "reminder time")]
    public void Parse_refuses_bad_input(string name, string kind, int target, string? remindAt, string message) =>
        Assert.Contains(message, Assert.Throws<ValidationException>(
            () => HabitRules.ParseHabit(name, "", kind, target, "", remindAt)).Message);

    [Fact]
    public void Switching_to_a_check_habit_keeps_only_completed_days()
    {
        var habit = With(NewHabit(HabitKind.Count, 8), 8, 0);
        With(habit, 3, 1);

        HabitRules.Apply(habit, new HabitInput("Water", "", HabitKind.Check, 1, "", null));

        Assert.Equal(new Dictionary<DateOnly, int> { [Today] = 1 }, habit.Log);
    }

    [Fact]
    public void Nudges_once_after_the_reminder_time_while_not_done()
    {
        var habit = NewHabit(remindAt: new TimeOnly(20, 0));

        Assert.False(HabitRules.ShouldNudge(habit, LocalTime(19, 59)));
        Assert.Single(HabitRules.CollectNudges([habit], LocalTime(20, 0)));
        Assert.Equal(Today, habit.NudgedOn);
        Assert.Empty(HabitRules.CollectNudges([habit], LocalTime(21, 0)));
        Assert.Single(HabitRules.CollectNudges([habit], LocalTime(20, 0, dayOffset: 1)));
    }

    [Fact]
    public void Never_nudges_when_done_or_without_a_time()
    {
        Assert.False(HabitRules.ShouldNudge(With(NewHabit(remindAt: new TimeOnly(8, 0)), 1, 0), LocalTime(9)));
        Assert.False(HabitRules.ShouldNudge(NewHabit(), LocalTime(23)));
    }

    [Fact]
    public void Snoozed_habits_nudge_again_when_the_snooze_ends()
    {
        var habit = NewHabit(remindAt: new TimeOnly(20, 0));
        habit.NudgedOn = Today;
        habit.SnoozedUntil = LocalTime(20, 10);

        Assert.Empty(HabitRules.CollectNudges([habit], LocalTime(20, 9)));
        Assert.Single(HabitRules.CollectNudges([habit], LocalTime(20, 10)));
        Assert.Null(habit.SnoozedUntil);
    }

    [Fact]
    public void A_snooze_into_tomorrow_is_kept_but_yesterdays_is_dropped()
    {
        var habit = NewHabit(remindAt: new TimeOnly(9, 0));
        habit.NudgedOn = Today;
        habit.SnoozedUntil = LocalTime(8, 0, dayOffset: 1);

        Assert.Empty(HabitRules.CollectNudges([habit], LocalTime(22, 0)));
        Assert.NotNull(habit.SnoozedUntil);

        habit.SnoozedUntil = LocalTime(23, 0, dayOffset: -1);
        habit.NudgedOn = Today.AddDays(-1);
        Assert.Empty(HabitRules.CollectNudges([habit], LocalTime(8, 0)));
        Assert.Null(habit.SnoozedUntil);
        Assert.Single(HabitRules.CollectNudges([habit], LocalTime(9, 0)));
    }

    [Fact]
    public void A_reminder_time_already_passed_today_starts_tomorrow()
    {
        var habit = NewHabit();

        HabitRules.SetRemindAt(habit, new TimeOnly(9, 0), LocalTime(15));
        Assert.Equal(Today, habit.NudgedOn);

        HabitRules.SetRemindAt(habit, new TimeOnly(18, 0), LocalTime(15));
        Assert.Null(habit.NudgedOn);
        Assert.Single(HabitRules.CollectNudges([habit], LocalTime(18)));
    }

    [Fact]
    public void Progress_describes_what_is_left()
    {
        var water = With(NewHabit(HabitKind.Count, 8), 3, 0);
        water.Unit = "glasses";

        Assert.Equal("3 / 8 glasses so far today", HabitRules.Progress(water, Today));
        Assert.Equal("Not done yet - keep your 2-day streak", HabitRules.Progress(With(NewHabit(), 1, 1, 2), Today));
        Assert.Equal("Not done yet today", HabitRules.Progress(NewHabit(), Today));
    }
}