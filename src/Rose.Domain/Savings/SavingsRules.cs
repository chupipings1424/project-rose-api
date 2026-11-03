using System.Globalization;
using Rose.Domain.Common;

namespace Rose.Domain.Savings;

/// <summary>
/// The savings maths. Pure functions: "today" is always passed in, so every rule is
/// easy to unit test without waiting for real days to pass.
/// </summary>
public static class SavingsRules
{
    /// <summary>The pace looks at the last 90 days of deposits...</summary>
    public const int PaceWindowDays = 90;

    /// <summary>...but never fewer than 30, so one early deposit does not promise the moon.</summary>
    public const int MinPaceDays = 30;

    public const decimal MaxAmount = 1_000_000_000_000m;

    private const decimal DaysPerMonth = 30.4375m; // 365.25 / 12
    private const int MaxForecastDays = 365 * 100;

    public static decimal Saved(SavingsGoal goal) => goal.Entries.Sum(e => e.Amount);

    /// <summary>How much to save each month (rounded up to the cent) to reach the target by the deadline.
    /// Null when the goal is reached, has no deadline, or the deadline has passed.</summary>
    public static decimal? MonthlyNeeded(decimal remaining, DateOnly? deadline, DateOnly today)
    {
        if (remaining <= 0 || deadline is not { } end || end < today) return null;
        var days = end.DayNumber - today.DayNumber + 1;
        var months = Math.Max(1m, Math.Ceiling(days / DaysPerMonth));
        return Math.Ceiling(remaining / months * 100m) / 100m;
    }

    /// <summary>Average amount saved per day recently (withdrawals count against it).</summary>
    public static decimal DailyPace(IEnumerable<SavingsEntry> entries, DateOnly today)
    {
        var from = today.AddDays(-(PaceWindowDays - 1));
        var recent = entries.Where(e => e.Date >= from && e.Date <= today).ToList();
        if (recent.Count == 0) return 0;
        var first = recent.Min(e => e.Date);
        var span = Math.Max(MinPaceDays, today.DayNumber - first.DayNumber + 1);
        return recent.Sum(e => e.Amount) / span;
    }

    /// <summary>When the goal is reached if you keep the current pace. Null when the pace is zero or negative.</summary>
    public static DateOnly? EstimatedFinish(decimal remaining, decimal dailyPace, DateOnly today)
    {
        if (remaining <= 0 || dailyPace <= 0) return null;
        var days = Math.Min(Math.Ceiling(remaining / dailyPace), MaxForecastDays);
        return today.AddDays((int)days);
    }

    public static string Status(decimal remaining, DateOnly? deadline, DateOnly? estimatedFinish, DateOnly today)
    {
        if (remaining <= 0) return GoalStatus.Done;
        if (deadline is not { } end) return GoalStatus.NoDeadline;
        if (end < today) return GoalStatus.Overdue;
        return estimatedFinish is { } finish && finish <= end ? GoalStatus.OnTrack : GoalStatus.Behind;
    }

    /// <summary>Everything the page shows for one goal.</summary>
    public static SavingsGoalView Summarize(SavingsGoal goal, DateOnly today)
    {
        var saved = Saved(goal);
        var remaining = Math.Max(0, goal.Target - saved);
        var finish = EstimatedFinish(remaining, DailyPace(goal.Entries, today), today);
        var percent = goal.Target <= 0 ? 0 : Math.Round((double)(saved / goal.Target) * 100, 1);
        return new SavingsGoalView(
            goal.Id,
            goal.Name,
            goal.Icon,
            goal.Target,
            goal.Deadline,
            saved,
            remaining,
            Math.Clamp(percent, 0, 100),
            MonthlyNeeded(remaining, goal.Deadline, today),
            finish,
            Status(remaining, goal.Deadline, finish, today),
            // Newest first; Reverse() first so the latest of two same-day entries comes on top (OrderBy is stable).
            [.. Enumerable.Reverse(goal.Entries).OrderByDescending(e => e.Date)],
            goal.CreatedAt,
            goal.UpdatedAt);
    }

    /// <summary>Validates an untrusted create / edit request.</summary>
    public static GoalInput ParseGoal(string? name, string? icon, decimal? target, string? deadline)
    {
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length == 0) throw new ValidationException("Give the goal a name.");
        if (cleanName.Length > 60) throw new ValidationException("Keep the name under 60 characters.");
        var cleanIcon = (icon ?? "").Trim();
        if (cleanIcon.Length > 8) throw new ValidationException("Use a single emoji or symbol as the icon.");
        if (target is not { } amount || amount <= 0) throw new ValidationException("The target must be more than zero.");
        if (amount > MaxAmount) throw new ValidationException("That target is too large.");
        return new GoalInput(cleanName, cleanIcon, Math.Round(amount, 2), ParseDate(deadline, "Pick a valid deadline."));
    }

    /// <summary>Validates a deposit or withdrawal. A withdrawal may not take more than is saved.</summary>
    public static EntryInput ParseEntry(string? kind, decimal? amount, string? date, string? note, decimal saved, DateOnly today)
    {
        var sign = kind switch
        {
            null or "" or "deposit" => 1,
            "withdraw" => -1,
            _ => throw new ValidationException("Choose deposit or withdraw."),
        };
        if (amount is not { } value || value <= 0) throw new ValidationException("Enter an amount above zero.");
        if (value > MaxAmount) throw new ValidationException("That amount is too large.");
        value = Math.Round(value, 2);

        var day = ParseDate(date, "Pick a valid date.") ?? today;
        if (day > today) throw new ValidationException("The date cannot be in the future.");
        if (sign < 0 && value > saved)
            throw new ValidationException($"You can withdraw at most {saved.ToString("0.##", CultureInfo.InvariantCulture)}.");

        var cleanNote = (note ?? "").Trim();
        if (cleanNote.Length > 100) throw new ValidationException("Keep the note under 100 characters.");
        return new EntryInput(day, sign * value, cleanNote);
    }

    private static DateOnly? ParseDate(string? text, string error)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new ValidationException(error);
    }
}