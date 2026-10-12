using Rose.Domain.Common;
using Rose.Domain.Reminders;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Domain;

public class ReminderRulesTests
{
    private static Reminder Make(string repeat, DateTimeOffset due, params int[] weekDays) => new()
    {
        Id = "r1",
        Text = "Test",
        DueAt = due,
        Repeat = repeat,
        WeekDays = [.. weekDays],
        CreatedAt = due,
        UpdatedAt = due,
    };

    private static DateTimeOffset Berlin(int y, int m, int d, int h, int min)
    {
        var local = new DateTime(y, m, d, h, min, 0);
        return new DateTimeOffset(local, Zones.Berlin.GetUtcOffset(local)).ToUniversalTime();
    }

    [Fact]
    public void One_off_reminders_have_no_next_occurrence() =>
        Assert.Null(ReminderRules.NextOccurrence(Make(RepeatRule.None, DateTimeOffset.UnixEpoch), DateTimeOffset.UnixEpoch, Zones.Berlin));

    [Fact]
    public void Daily_keeps_local_time_across_the_spring_dst_change()
    {
        // 07:30 in Berlin is 06:30Z in winter and 05:30Z in summer.
        var reminder = Make(RepeatRule.Daily, Berlin(2026, 3, 28, 7, 30));

        var next = ReminderRules.NextOccurrence(reminder, reminder.DueAt, Zones.Berlin);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 5, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void A_time_inside_the_dst_gap_moves_an_hour_later()
    {
        // 02:30 does not exist on 29 March 2026 in Berlin.
        var reminder = Make(RepeatRule.Daily, Berlin(2026, 3, 28, 2, 30));

        var next = ReminderRules.NextOccurrence(reminder, reminder.DueAt, Zones.Berlin);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), next); // 03:30 CEST
    }

    [Fact]
    public void Weekdays_skip_the_weekend()
    {
        var friday = Berlin(2026, 10, 9, 8, 0);

        var next = ReminderRules.NextOccurrence(Make(RepeatRule.Weekdays, friday), friday, Zones.Berlin);

        Assert.Equal(Berlin(2026, 10, 12, 8, 0), next); // Monday
    }

    [Fact]
    public void Weekly_rings_on_the_chosen_days()
    {
        var monday = Berlin(2026, 10, 12, 8, 0);
        var reminder = Make(RepeatRule.Weekly, monday, 1, 4); // Mon, Thu

        var next = ReminderRules.NextOccurrence(reminder, monday, Zones.Berlin);

        Assert.Equal(Berlin(2026, 10, 15, 8, 0), next);
        Assert.Equal("Every Mon, Thu", ReminderRules.DescribeRepeat(reminder, Zones.Berlin));
    }

    [Fact]
    public void Monthly_on_the_31st_uses_the_last_day_of_short_months()
    {
        var reminder = Make(RepeatRule.Monthly, Berlin(2026, 1, 31, 9, 0));

        var next = ReminderRules.NextOccurrence(reminder, reminder.DueAt, Zones.Berlin);

        Assert.Equal(Berlin(2026, 2, 28, 9, 0), next);
        Assert.Equal("Monthly on day 31", ReminderRules.DescribeRepeat(reminder, Zones.Berlin));
    }

    [Fact]
    public void CollectDue_fires_a_one_off_once()
    {
        var due = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var reminder = Make(RepeatRule.None, due);

        var first = ReminderRules.CollectDue([reminder], due.AddSeconds(5), Zones.Berlin);
        reminder.Ringing = false;
        var second = ReminderRules.CollectDue([reminder], due.AddMinutes(5), Zones.Berlin);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal(due, reminder.FiredFor);
    }

    [Fact]
    public void CollectDue_moves_a_repeating_reminder_forward()
    {
        var due = Berlin(2026, 10, 12, 8, 0);
        var reminder = Make(RepeatRule.Daily, due);

        var fired = ReminderRules.CollectDue([reminder], due.AddSeconds(1), Zones.Berlin);

        Assert.Single(fired);
        Assert.True(reminder.Ringing);
        Assert.Equal(Berlin(2026, 10, 13, 8, 0), reminder.DueAt);
    }

    [Fact]
    public void CollectDue_rings_again_when_a_snooze_ends()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var reminder = Make(RepeatRule.None, now.AddDays(1));
        reminder.SnoozedUntil = now;

        Assert.Single(ReminderRules.CollectDue([reminder], now, Zones.Berlin));
        Assert.Null(reminder.SnoozedUntil);
    }

    [Theory]
    [InlineData("  ", "2026-01-01T00:00:00Z", "none", "Write what to remind you about.")]
    [InlineData("x", "nope", "none", "Pick a valid date and time.")]
    [InlineData("x", "2026-01-01T00:00:00Z", "hourly", "Unknown repeat rule.")]
    [InlineData("x", "2026-01-01T00:00:00Z", "weekly", "Pick at least one weekday.")]
    public void Parse_rejects_bad_input(string text, string due, string repeat, string message) =>
        Assert.Equal(message, Assert.Throws<ValidationException>(() => ReminderRules.Parse(text, due, repeat, null)).Message);

    [Fact]
    public void Parse_cleans_input()
    {
        var input = ReminderRules.Parse("  Gym ", "2026-01-01T07:30:00.1234567Z", "weekly", [5, 1, 1, 9]);

        Assert.Equal("Gym", input.Text);
        Assert.Equal([1, 5], input.WeekDays);
        Assert.Equal(123, input.DueAt.Millisecond);
        Assert.Equal(0, input.DueAt.Ticks % TimeSpan.TicksPerMillisecond);
    }
}