using Rose.Application.Reminders;
using Rose.Domain.Common;
using Rose.Domain.Habits;
using Rose.Domain.Health;
using Rose.Domain.Loans;
using Rose.Domain.Preferences;
using Rose.Domain.Reminders;
using Rose.Domain.Savings;

namespace Rose.Infrastructure.Persistence;

/// <summary>Domain object &lt;-&gt; database row. PostgreSQL stores instants in UTC, so they are converted on the way in.</summary>
internal static class Mappers
{
    public static ReminderRow ToRow(Reminder r) => new()
    {
        Id = r.Id,
        Text = r.Text,
        DueAt = r.DueAt.ToUniversalTime(),
        Repeat = r.Repeat,
        WeekDays = [.. r.WeekDays],
        SnoozedUntil = r.SnoozedUntil?.ToUniversalTime(),
        Ringing = r.Ringing,
        FiredFor = r.FiredFor?.ToUniversalTime(),
        LastFiredAt = r.LastFiredAt?.ToUniversalTime(),
        Done = r.Done,
        CreatedAt = r.CreatedAt.ToUniversalTime(),
        UpdatedAt = r.UpdatedAt.ToUniversalTime(),
    };

    public static Reminder ToDomain(ReminderRow r) => new()
    {
        Id = r.Id,
        Text = r.Text,
        DueAt = r.DueAt,
        Repeat = r.Repeat,
        WeekDays = [.. r.WeekDays],
        SnoozedUntil = r.SnoozedUntil,
        Ringing = r.Ringing,
        FiredFor = r.FiredFor,
        LastFiredAt = r.LastFiredAt,
        Done = r.Done,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    public static PushDeviceRow ToRow(PushDevice d) => new()
    {
        Endpoint = d.Subscription.Endpoint!,
        P256dh = d.Subscription.Keys?.P256dh ?? "",
        Auth = d.Subscription.Keys?.Auth ?? "",
        ExpirationTime = d.Subscription.ExpirationTime,
        Label = d.Label,
        CreatedAt = d.CreatedAt.ToUniversalTime(),
    };

    public static PushDevice ToDomain(PushDeviceRow d) =>
        new(new PushSubscriptionInfo(d.Endpoint, d.ExpirationTime, new PushKeys(d.P256dh, d.Auth)), d.Label, d.CreatedAt);

    public static SavingsGoalRow ToRow(SavingsGoal g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Icon = g.Icon,
        Target = g.Target,
        Deadline = g.Deadline,
        CreatedAt = g.CreatedAt.ToUniversalTime(),
        UpdatedAt = g.UpdatedAt.ToUniversalTime(),
        Entries = [.. g.Entries.Select((e, i) => new SavingsEntryRow
        {
            Id = e.Id,
            GoalId = g.Id,
            Position = i,
            Date = e.Date,
            Amount = e.Amount,
            Note = e.Note,
        })],
    };

    public static SavingsGoal ToDomain(SavingsGoalRow g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Icon = g.Icon,
        Target = g.Target,
        Deadline = g.Deadline,
        CreatedAt = g.CreatedAt,
        UpdatedAt = g.UpdatedAt,
        Entries = [.. g.Entries.OrderBy(e => e.Position).Select(e => new SavingsEntry(e.Id, e.Date, e.Amount, e.Note))],
    };

public static LoanRow ToRow(Loan l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Type = l.Type,
        LoanDate = l.LoanDate,
        Term = l.Term,
        MonthlyAmortization = l.MonthlyAmortization,
        Amount = l.Amount,
        CreatedAt = l.CreatedAt.ToUniversalTime(),
        UpdatedAt = l.UpdatedAt.ToUniversalTime(),
    };

    public static Loan ToDomain(LoanRow l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Type = l.Type,
        LoanDate = l.LoanDate,
        Term = l.Term,
        MonthlyAmortization = l.MonthlyAmortization,
        Amount = l.Amount,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt,
    };

    public static HabitRow ToRow(Habit h) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Icon = h.Icon,
        Kind = h.Kind,
        Target = h.Target,
        Unit = h.Unit,
        RemindAt = h.RemindAt,
        NudgedOn = h.NudgedOn,
        SnoozedUntil = h.SnoozedUntil?.ToUniversalTime(),
        CreatedAt = h.CreatedAt.ToUniversalTime(),
        UpdatedAt = h.UpdatedAt.ToUniversalTime(),
        Days = [.. h.Log.Select(p => new HabitDayRow { HabitId = h.Id, Date = p.Key, Value = p.Value })],
    };

    public static Habit ToDomain(HabitRow h) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Icon = h.Icon,
        Kind = h.Kind,
        Target = h.Target,
        Unit = h.Unit,
        RemindAt = h.RemindAt,
        NudgedOn = h.NudgedOn,
        SnoozedUntil = h.SnoozedUntil,
        CreatedAt = h.CreatedAt,
        UpdatedAt = h.UpdatedAt,
        Log = h.Days.ToDictionary(d => d.Date, d => d.Value),
    };

    public static AppPrefsRow ToRow(AppPrefs p) => new() { Id = p.Id, Hidden = [.. p.Hidden], UpdatedAt = p.UpdatedAt.ToUniversalTime() };

    public static AppPrefs ToDomain(AppPrefsRow p) => new() { Id = p.Id, Hidden = [.. p.Hidden], UpdatedAt = p.UpdatedAt };

    public static HealthProfileRow ToRow(HealthProfile p) => new()
    {
        Id = p.Id,
        HeightCm = p.HeightCm,
        BirthYear = p.BirthYear,
        Sex = p.Sex,
        GoalKg = p.GoalKg,
        UpdatedAt = p.UpdatedAt.ToUniversalTime(),
    };

    public static HealthProfile ToDomain(HealthProfileRow p) => new()
    {
        Id = p.Id,
        HeightCm = p.HeightCm,
        BirthYear = p.BirthYear,
        Sex = p.Sex,
        GoalKg = p.GoalKg,
        UpdatedAt = p.UpdatedAt,
    };

    public static HealthDayRow ToRow(HealthDay d) => new()
    {
        Id = d.Id,
        Date = d.Date,
        WeightKg = d.WeightKg,
        WalkKm = d.WalkKm,
        FastingHours = d.FastingHours,
        UpdatedAt = d.UpdatedAt.ToUniversalTime(),
        Meals = [.. d.Meals.Select(m => new HealthMealRow { DayId = d.Id, Id = m.Id, Name = m.Name, Kcal = m.Kcal, At = m.At.ToUniversalTime() })],
    };

    public static HealthDay ToDomain(HealthDayRow d) => new()
    {
        Id = d.Id,
        Date = d.Date,
        WeightKg = d.WeightKg,
        WalkKm = d.WalkKm,
        FastingHours = d.FastingHours,
        UpdatedAt = d.UpdatedAt,
        Meals = [.. d.Meals.OrderBy(m => m.At).Select(m => new HealthMeal(m.Id, m.Name, m.Kcal, m.At))],
    };

    /// <summary>ISO date text ("" = none) -> date column.</summary>
    public static DateOnly? Date(string iso) => Coerce.ToDate(iso);

    /// <summary>Date column -> ISO date text ("" = none), as the tracker domain expects.</summary>
    public static string Iso(DateOnly? date) => date is { } d ? Coerce.IsoDate(d) : "";
}