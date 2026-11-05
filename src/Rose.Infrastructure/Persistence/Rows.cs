namespace Rose.Infrastructure.Persistence;

// Database rows. Deliberately separate from the domain types: the domain stays free of
// persistence concerns and the table layout can change without touching business rules.
// Mappers.cs converts between the two.

/// <summary>A top-level row that belongs to one user (child rows belong to theirs through the parent).</summary>
internal interface IOwnedRow
{
    /// <summary>The user's id. <see cref="Guid.Empty"/> = saved before ROSE had accounts (claimed by the first admin).</summary>
    Guid OwnerId { get; set; }
}

internal sealed class AccountRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public long Seq { get; set; }
    public string Title { get; set; } = "";
    public double TotalAmount { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Frequency { get; set; } = "";
    public int Instalments { get; set; }
    public double PaymentAmount { get; set; }
    public string Notes { get; set; } = "";
}

internal sealed class PaymentRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public string Title { get; set; } = "";
    public double Amount { get; set; }
    public DateOnly? Date { get; set; }
    public string Time { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";

    // What the payment added to the budget (all null = nothing); see BudgetMark.
    public int? BudgetYear { get; set; }
    public int? BudgetMonth { get; set; }
    public double? BudgetAmount { get; set; }
}

internal sealed class BudgetLineRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public int Year { get; set; }
    public string Section { get; set; } = "";
    public string Group { get; set; } = "";
    public string Item { get; set; } = "";
    public double[] Amounts { get; set; } = new double[12];
    public string Notes { get; set; } = "";
}

internal sealed class ReminderRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public string Repeat { get; set; } = "";
    public int[] WeekDays { get; set; } = [];
    public DateTimeOffset? SnoozedUntil { get; set; }
    public bool Ringing { get; set; }
    public DateTimeOffset? FiredFor { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class PushDeviceRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public long? ExpirationTime { get; set; }
    public string Label { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SavingsGoalRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public decimal Target { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<SavingsEntryRow> Entries { get; set; } = [];
}

internal sealed class SavingsEntryRow
{
    public string Id { get; set; } = "";
    public string GoalId { get; set; } = "";
    public int Position { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}

internal sealed class HabitRow : IOwnedRow
{
    public Guid OwnerId { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Target { get; set; }
    public string Unit { get; set; } = "";
    public TimeOnly? RemindAt { get; set; }
    public DateOnly? NudgedOn { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<HabitDayRow> Days { get; set; } = [];
}

internal sealed class HabitDayRow
{
    public string HabitId { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Value { get; set; }
}

/// <summary>A ROSE account; <see cref="Id"/> is the Supabase user id.</summary>
internal sealed class UserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Small server-side settings, e.g. the VAPID keys when they are not configured.</summary>
internal sealed class AppSecretRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}