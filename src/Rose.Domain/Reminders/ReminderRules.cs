using System.Globalization;
using Rose.Domain.Common;

namespace Rose.Domain.Reminders;

/// <summary>
/// Scheduling rules. Pure functions: "now" and the time zone are passed in, so
/// every rule is easy to unit test (including daylight-saving changes).
/// </summary>
public static class ReminderRules
{
    public static readonly IReadOnlyList<string> WeekdayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>Drops sub-millisecond precision, so times round-trip exactly through ISO text.</summary>
    public static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new DateTimeOffset(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    /// <summary>The first time strictly after <paramref name="after"/> that a repeating reminder rings,
    /// keeping its local time of day. Null for one-off reminders.</summary>
    public static DateTimeOffset? NextOccurrence(Reminder reminder, DateTimeOffset after, TimeZoneInfo zone)
    {
        if (reminder.Repeat == RepeatRule.None) return null;
        var anchor = TimeZoneInfo.ConvertTime(reminder.DueAt, zone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        var timeOfDay = new TimeOnly(anchor.Hour, anchor.Minute);

        // Any rule matches within ~31 days; 400 is a generous safety net.
        for (var i = 0; i < 400; i++, day = day.AddDays(1))
        {
            var candidate = ToInstant(day.ToDateTime(timeOfDay), zone);
            if (candidate > after && DayMatches(reminder, anchor, day)) return candidate;
        }
        return after.AddDays(1);
    }

    private static bool DayMatches(Reminder reminder, DateTimeOffset anchor, DateOnly day) => reminder.Repeat switch
    {
        RepeatRule.Daily => true,
        RepeatRule.Weekdays => day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
        RepeatRule.Weekly => (reminder.WeekDays.Count > 0 ? reminder.WeekDays : [(int)anchor.DayOfWeek])
            .Contains((int)day.DayOfWeek),
        // The 31st rings on the last day of shorter months instead of skipping them.
        RepeatRule.Monthly => day.Day == Math.Min(anchor.Day, DateTime.DaysInMonth(day.Year, day.Month)),
        _ => false,
    };

    /// <summary>Local wall-clock time to an instant. Skipped (DST gap) times move an hour later;
    /// repeated ones use the first occurrence - the same choices JavaScript dates make.</summary>
    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>One scheduler step: returns what must ring now and updates those reminders in place.</summary>
    public static List<Reminder> CollectDue(IEnumerable<Reminder> reminders, DateTimeOffset now, TimeZoneInfo zone)
    {
        var fired = new List<Reminder>();
        foreach (var reminder in reminders)
        {
            if (reminder.Done) continue;
            var ring = false;

            if (reminder.SnoozedUntil is { } snoozed && snoozed <= now)
            {
                reminder.SnoozedUntil = null;
                ring = true;
            }

            if (reminder.DueAt <= now && reminder.FiredFor != reminder.DueAt)
            {
                ring = true;
                if (NextOccurrence(reminder, now, zone) is { } next) reminder.DueAt = next;
                else reminder.FiredFor = reminder.DueAt;
            }

            if (ring)
            {
                reminder.Ringing = true;
                reminder.LastFiredAt = now;
                reminder.UpdatedAt = now;
                fired.Add(reminder);
            }
        }
        return fired;
    }

    /// <summary>"Every weekday", "Every Mon, Thu", "Monthly on day 31".</summary>
    public static string DescribeRepeat(Reminder reminder, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(reminder.DueAt, zone);
        return reminder.Repeat switch
        {
            RepeatRule.Daily => "Every day",
            RepeatRule.Weekdays => "Every weekday",
            RepeatRule.Weekly => "Every " + string.Join(", ",
                (reminder.WeekDays.Count > 0 ? reminder.WeekDays : [(int)local.DayOfWeek]).Order().Select(d => WeekdayNames[d])),
            RepeatRule.Monthly => $"Monthly on day {local.Day}",
            _ => "Once",
        };
    }

    /// <summary>Validates untrusted input and returns a clean copy.</summary>
    public static ReminderInput Parse(string? text, string? dueAt, string? repeat, IEnumerable<int>? weekDays)
    {
        var clean = (text ?? "").Trim();
        if (clean.Length == 0) throw new ValidationException("Write what to remind you about.");
        if (clean.Length > 200) throw new ValidationException("Keep the reminder under 200 characters.");
        if (!DateTimeOffset.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var due))
            throw new ValidationException("Pick a valid date and time.");
        var rule = repeat ?? RepeatRule.None;
        if (!RepeatRule.All.Contains(rule)) throw new ValidationException("Unknown repeat rule.");
        var days = (weekDays ?? []).Where(d => d is >= 0 and <= 6).Distinct().Order().ToList();
        if (rule == RepeatRule.Weekly && days.Count == 0) throw new ValidationException("Pick at least one weekday.");
        return new ReminderInput(clean, TruncateToMilliseconds(due), rule, days);
    }
}