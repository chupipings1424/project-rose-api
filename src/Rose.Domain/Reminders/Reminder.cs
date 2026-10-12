namespace Rose.Domain.Reminders;

/// <summary>A reminder. Mutable on purpose: the service changes it in place and then saves the list.</summary>
public sealed class Reminder
{
    public required string Id { get; init; }
    public required string Text { get; set; }

    /// <summary>Next time it rings. Repeating reminders move it forward after each ring.</summary>
    public required DateTimeOffset DueAt { get; set; }

    public required string Repeat { get; set; }

    /// <summary>For "weekly": 0 = Sunday ... 6 = Saturday.</summary>
    public List<int> WeekDays { get; set; } = [];

    /// <summary>A one-off "ring again at" time set by snooze.</summary>
    public DateTimeOffset? SnoozedUntil { get; set; }

    /// <summary>True after it rang, until you snooze, finish or dismiss it.</summary>
    public bool Ringing { get; set; }

    /// <summary>One-off reminders: the due time that already rang, so it never rings twice.</summary>
    public DateTimeOffset? FiredFor { get; set; }

    public DateTimeOffset? LastFiredAt { get; set; }
    public bool Done { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>A detached snapshot, safe to hand out while the original keeps changing.</summary>
    public Reminder Copy()
    {
        var copy = (Reminder)MemberwiseClone();
        copy.WeekDays = [.. WeekDays];
        return copy;
    }
}

/// <summary>A validated create / edit request.</summary>
public sealed record ReminderInput(string Text, DateTimeOffset DueAt, string Repeat, IReadOnlyList<int> WeekDays);

public static class RepeatRule
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekdays = "weekdays";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [None, Daily, Weekdays, Weekly, Monthly];
}