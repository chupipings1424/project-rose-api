namespace Rose.Domain.Savings;

/// <summary>A savings goal and every deposit / withdrawal made towards it.
/// Mutable on purpose: the service changes it in place and then saves the list.</summary>
public sealed class SavingsGoal
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    /// <summary>An emoji or symbol shown on the card (optional).</summary>
    public string Icon { get; set; } = "";

    public required decimal Target { get; set; }
    public DateOnly? Deadline { get; set; }

    /// <summary>Oldest first. Deposits are positive, withdrawals negative.</summary>
    public List<SavingsEntry> Entries { get; set; } = [];

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One deposit (positive <see cref="Amount"/>) or withdrawal (negative).</summary>
public sealed record SavingsEntry(string Id, DateOnly Date, decimal Amount, string Note);

/// <summary>A validated create / edit request.</summary>
public sealed record GoalInput(string Name, string Icon, decimal Target, DateOnly? Deadline);

/// <summary>A validated deposit / withdrawal. <see cref="Amount"/> is already signed.</summary>
public sealed record EntryInput(DateOnly Date, decimal Amount, string Note);

public static class GoalStatus
{
    public const string Done = "done";
    public const string OnTrack = "on-track";
    public const string Behind = "behind";
    public const string Overdue = "overdue";
    public const string NoDeadline = "no-deadline";
}

/// <summary>A goal plus everything calculated from it - what the API returns.</summary>
public sealed record SavingsGoalView(
    string Id,
    string Name,
    string Icon,
    decimal Target,
    DateOnly? Deadline,
    decimal Saved,
    decimal Remaining,
    double Percent,
    decimal? MonthlyNeeded,
    DateOnly? EstimatedFinish,
    string Status,
    IReadOnlyList<SavingsEntry> Entries,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);