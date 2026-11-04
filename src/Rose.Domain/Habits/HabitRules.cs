using System.Globalization;
using Rose.Domain.Common;

namespace Rose.Domain.Habits;

/// <summary>Pure habit logic: validation, streaks and when to nudge. No I/O, so it is easy to test.</summary>
public static class HabitRules
{
    public const int MaxCount = 9999;

    /// <summary>You can tick today and the 6 days before it - enough to catch up after a weekend.</summary>
    public const int EditableDays = 7;

    /// <summary>How many days of log the API returns: 13 weeks, so a 12-week heatmap can start on any weekday.</summary>
    public const int HistoryDays = 91;

    public static bool IsComplete(Habit habit, DateOnly day) =>
        habit.Log.TryGetValue(day, out var value) && value >= habit.Target;

    /// <summary>
    /// Consecutive completed days ending today. If today isn't done *yet*, the streak is still
    /// alive and counts back from yesterday - it only breaks once a whole day is missed.
    /// </summary>
    public static int CurrentStreak(Habit habit, DateOnly today)
    {
        var day = IsComplete(habit, today) ? today : today.AddDays(-1);
        var streak = 0;
        while (IsComplete(habit, day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    public static int BestStreak(Habit habit)
    {
        int best = 0, run = 0;
        DateOnly? previous = null;
        foreach (var day in habit.Log.Keys.Where(d => IsComplete(habit, d)).Order())
        {
            run = previous is { } p && p.AddDays(1) == day ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }
        return best;
    }

    public static HabitView Summarize(Habit habit, DateOnly today)
    {
        var from = today.AddDays(-(HistoryDays - 1));
        return new HabitView(
            habit.Id,
            habit.Name,
            habit.Icon,
            habit.Kind,
            habit.Target,
            habit.Unit,
            habit.RemindAt?.ToString("HH:mm", CultureInfo.InvariantCulture),
            habit.Log.GetValueOrDefault(today),
            IsComplete(habit, today),
            CurrentStreak(habit, today),
            BestStreak(habit),
            habit.Log.Where(e => e.Key >= from && e.Key <= today).OrderBy(e => e.Key).ToDictionary(),
            habit.CreatedAt,
            habit.UpdatedAt);
    }

    /// <summary>The nudge text, e.g. "3 / 8 glasses so far" or "Not done yet - 5-day streak".</summary>
    public static string Progress(Habit habit, DateOnly today)
    {
        if (habit.Kind == HabitKind.Count)
        {
            var unit = habit.Unit.Length > 0 ? $" {habit.Unit}" : "";
            return $"{habit.Log.GetValueOrDefault(today)} / {habit.Target}{unit} so far today";
        }
        var streak = CurrentStreak(habit, today);
        return streak > 0 ? $"Not done yet - keep your {streak}-day streak" : "Not done yet today";
    }

    public static HabitInput ParseHabit(string? name, string? icon, string? kind, int? target, string? unit, string? remindAt)
    {
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length == 0) throw new ValidationException("Give the habit a name.");
        if (cleanName.Length > 60) throw new ValidationException("Keep the name under 60 characters.");
        var cleanIcon = (icon ?? "").Trim();
        if (cleanIcon.Length > 8) throw new ValidationException("Use a single emoji or symbol as the icon.");

        var cleanKind = kind switch
        {
            null or "" or HabitKind.Check => HabitKind.Check,
            HabitKind.Count => HabitKind.Count,
            _ => throw new ValidationException("Choose a check or a count habit."),
        };
        var cleanTarget = 1;
        var cleanUnit = "";
        if (cleanKind == HabitKind.Count)
        {
            if (target is not { } goal || goal < 1 || goal > MaxCount)
                throw new ValidationException($"The daily target must be between 1 and {MaxCount}.");
            cleanTarget = goal;
            cleanUnit = (unit ?? "").Trim();
            if (cleanUnit.Length > 20) throw new ValidationException("Keep the unit under 20 characters.");
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(remindAt))
        {
            time = TimeOnly.TryParseExact(remindAt.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? new TimeOnly(parsed.Hour, parsed.Minute)
                : throw new ValidationException("Pick a valid reminder time.");
        }
        return new HabitInput(cleanName, cleanIcon, cleanKind, cleanTarget, cleanUnit, time);
    }

    /// <summary>Only today and the 6 days before it can be changed - no future, no rewriting old history.</summary>
    public static DateOnly ParseDay(string? text, DateOnly today)
    {
        if (!DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new ValidationException("Pick a valid date.");
        if (day > today) throw new ValidationException("You can't tick a day that hasn't happened yet.");
        if (day < today.AddDays(-(EditableDays - 1)))
            throw new ValidationException($"Only the last {EditableDays} days can be changed.");
        return day;
    }

    public static int ParseValue(Habit habit, int? value)
    {
        if (value is not { } amount) throw new ValidationException("Send a value.");
        var max = habit.Kind == HabitKind.Check ? 1 : MaxCount;
        if (amount < 0 || amount > max)
            throw new ValidationException(habit.Kind == HabitKind.Check
                ? "A check habit is either 0 (not done) or 1 (done)."
                : $"The count must be between 0 and {MaxCount}.");
        return amount;
    }

    /// <summary>Sets one day. Zero removes the day, so the log only ever holds days you did something.</summary>
    public static void SetDay(Habit habit, DateOnly day, int value)
    {
        if (value == 0) habit.Log.Remove(day);
        else habit.Log[day] = value;
    }

    /// <summary>
    /// Applies an edit. Switching to a check habit turns each completed day into a tick and
    /// drops partial days, so streaks mean the same thing before and after.
    /// </summary>
    public static void Apply(Habit habit, HabitInput input)
    {
        if (habit.Kind != HabitKind.Check && input.Kind == HabitKind.Check)
        {
            habit.Log = habit.Log
                .Where(e => e.Value >= habit.Target)
                .ToDictionary(e => e.Key, _ => 1);
        }
        habit.Name = input.Name;
        habit.Icon = input.Icon;
        habit.Kind = input.Kind;
        habit.Target = input.Target;
        habit.Unit = input.Unit;
    }

    /// <summary>
    /// Sets (or clears) the nudge time. If that time already passed today, the first nudge
    /// is tomorrow - setting a 9:00 reminder at 15:00 shouldn't pop up straight away.
    /// </summary>
    public static void SetRemindAt(Habit habit, TimeOnly? remindAt, DateTimeOffset localNow)
    {
        if (habit.RemindAt == remindAt) return;
        habit.RemindAt = remindAt;
        habit.SnoozedUntil = null;
        habit.NudgedOn = remindAt is { } time && TimeOnly.FromDateTime(localNow.DateTime) >= time
            ? DateOnly.FromDateTime(localNow.DateTime)
            : null;
    }

    /// <summary>Should this habit nudge you right now? <paramref name="localNow"/> is the server's local time.</summary>
    public static bool ShouldNudge(Habit habit, DateTimeOffset localNow)
    {
        if (habit.RemindAt is not { } remindAt) return false;
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (IsComplete(habit, today)) return false;
        if (habit.SnoozedUntil is { } snoozed && !IsStale(snoozed, localNow)) return localNow >= snoozed;
        return habit.NudgedOn != today && TimeOnly.FromDateTime(localNow.DateTime) >= remindAt;
    }

    /// <summary>Called by the scheduler: marks what nudges now (once a day) and returns it.</summary>
    public static IReadOnlyList<HabitNudge> CollectNudges(IEnumerable<Habit> habits, DateTimeOffset localNow)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var due = new List<HabitNudge>();
        foreach (var habit in habits)
        {
            // A snooze from yesterday (e.g. the PC was off) is stale - today follows the normal time.
            if (habit.SnoozedUntil is { } snoozed && IsStale(snoozed, localNow)) habit.SnoozedUntil = null;
            if (!ShouldNudge(habit, localNow)) continue;
            var afterSnooze = habit.SnoozedUntil is not null;
            habit.NudgedOn = today;
            habit.SnoozedUntil = null;
            due.Add(new HabitNudge(habit, afterSnooze));
        }
        return due;
    }

    /// <summary>A snooze that ended on an earlier day than today.</summary>
    private static bool IsStale(DateTimeOffset snoozedUntil, DateTimeOffset localNow) =>
        DateOnly.FromDateTime(snoozedUntil.ToOffset(localNow.Offset).DateTime) < DateOnly.FromDateTime(localNow.DateTime);
}