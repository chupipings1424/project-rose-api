namespace Rose.Domain.Habits;

/// <summary>A habit and its day-by-day log. Mutable on purpose: the service changes it in place and then saves the list.</summary>
public sealed class Habit
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    /// <summary>An emoji or symbol shown on the card (optional).</summary>
    public string Icon { get; set; } = "";

    /// <summary><see cref="HabitKind.Check"/> (done / not done) or <see cref="HabitKind.Count"/> (e.g. 8 glasses).</summary>
    public required string Kind { get; set; }

    /// <summary>How much counts as "done" for one day. Always 1 for check habits.</summary>
    public required int Target { get; set; }

    /// <summary>What a count habit counts, e.g. "glasses". Empty for check habits.</summary>
    public string Unit { get; set; } = "";

    /// <summary>Local time of day to nudge you if it isn't done yet (optional).</summary>
    public TimeOnly? RemindAt { get; set; }

    /// <summary>Day -> amount done. Days with nothing done are simply missing.</summary>
    public Dictionary<DateOnly, int> Log { get; set; } = [];

    /// <summary>The day the daily nudge last went out, so it goes out at most once a day.</summary>
    public DateOnly? NudgedOn { get; set; }

    /// <summary>A "nudge me again at" time set by snoozing the nudge.</summary>
    public DateTimeOffset? SnoozedUntil { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>A detached snapshot, safe to hand out while the original keeps changing.</summary>
    public Habit Copy()
    {
        var copy = (Habit)MemberwiseClone();
        copy.Log = new Dictionary<DateOnly, int>(Log);
        return copy;
    }
}

public static class HabitKind
{
    public const string Check = "check";
    public const string Count = "count";
}

/// <summary>A validated create / edit request.</summary>
public sealed record HabitInput(string Name, string Icon, string Kind, int Target, string Unit, TimeOnly? RemindAt);

/// <summary>A habit plus everything calculated from it - what the API returns.</summary>
/// <param name="RemindAt">"HH:mm", or null when there is no nudge.</param>
/// <param name="Today">Amount done today.</param>
/// <param name="Log">Only the recent days (enough for the 12-week heatmap).</param>
public sealed record HabitView(
    string Id,
    string Name,
    string Icon,
    string Kind,
    int Target,
    string Unit,
    string? RemindAt,
    int Today,
    bool DoneToday,
    int CurrentStreak,
    int BestStreak,
    IReadOnlyDictionary<DateOnly, int> Log,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);