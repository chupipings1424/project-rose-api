namespace Rose.Infrastructure.Persistence;

// Database rows. Deliberately separate from the domain types: the domain stays free of
// persistence concerns and the table layout can change without touching business rules.
// Mappers.cs converts between the two.

internal sealed class AccountRow
{
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

internal sealed class PaymentRow
{
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public string Title { get; set; } = "";
    public double Amount { get; set; }
    public DateOnly? Date { get; set; }
    public string Time { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";
}

internal sealed class BudgetLineRow
{
    public string Id { get; set; } = "";
    public long Seq { get; set; }
    public int Year { get; set; }
    public string Section { get; set; } = "";
    public string Group { get; set; } = "";
    public string Item { get; set; } = "";
    public double[] Amounts { get; set; } = new double[12];
    public string Notes { get; set; } = "";
}

internal sealed class ReminderRow
{
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

internal sealed class PushDeviceRow
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public long? ExpirationTime { get; set; }
    public string Label { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SavingsGoalRow
{
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

internal sealed class HabitRow
{
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

/// <summary>Small server-side settings, e.g. the VAPID keys when they are not configured.</summary>
internal sealed class AppSecretRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}