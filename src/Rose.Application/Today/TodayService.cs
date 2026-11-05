using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Application.Habits;
using Rose.Application.Reminders;
using Rose.Application.Tracker;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Application.Today;

/// <summary>The Today page in one call: payments due soon, today's reminders, habits and this month's budget.</summary>
public sealed class TodayService(
    ITrackerWorkbookStore store, ReminderService reminders, HabitService habits, ICurrentUser user, TimeProvider clock)
{
    /// <summary>Unpaid payments due within this many days (and every overdue one) are listed.</summary>
    public const int PaymentWindowDays = 7;

    public async Task<TodayView> GetAsync(CancellationToken ct = default)
    {
        var now = clock.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);

        TodayBudget budget;
        IReadOnlyList<TodayPayment> payments;
        using (var book = await store.OpenAsync(user.Id, ct))
        {
            await PaymentBudgetSync.SyncAsync(book, today, ct);
            payments = DuePayments(book.Payments(), today);
            budget = MonthBudget(BudgetView.Build(today.Year, book.BudgetLines(), today.Year), today.Month - 1);
        }

        var dueReminders = (await reminders.ListAsync(user.Id, ct))
            .Where(r => !r.Done)
            .Where(r => r.Ringing || DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.DueAt, clock.LocalTimeZone).DateTime) <= today)
            .Select(r => new TodayReminder(r.Id, r.Text, r.DueAt, r.Ringing, r.DueAt < now))
            .ToList();

        var todaysHabits = (await habits.ListAsync(user.Id, ct))
            .Select(h => new TodayHabit(h.Id, h.Name, h.Icon, h.Kind, h.Today, h.Target, h.Unit, h.DoneToday))
            .ToList();

        return new TodayView(Coerce.IsoDate(today), budget, payments, dueReminders, todaysHabits);
    }

    internal static List<TodayPayment> DuePayments(IEnumerable<Payment> all, DateOnly today) =>
        [.. all
            .Where(p => !p.IsPaid())
            .Select(p => (Payment: p, Due: Coerce.ToDate(p.DueOrDate())))
            .Where(x => x.Due is { } due && due.DayNumber - today.DayNumber <= PaymentWindowDays)
            .OrderBy(x => x.Due)
            .Select(x => new TodayPayment(
                x.Payment.Id, x.Payment.Title, Coerce.Round(x.Payment.Amount), Coerce.IsoDate(x.Due!.Value),
                x.Due.Value.DayNumber - today.DayNumber))];

    internal static TodayBudget MonthBudget(BudgetView view, int month)
    {
        var revenue = view.Section(BudgetRules.Revenue).Monthly[month];
        var expenses = view.Section(BudgetRules.Expense).Monthly[month];
        return new TodayBudget(view.Year, month, BudgetRules.Months[month], revenue, expenses, Coerce.Round(revenue - expenses));
    }
}

public sealed record TodayView(
    string Date,
    TodayBudget Budget,
    IReadOnlyList<TodayPayment> Payments,
    IReadOnlyList<TodayReminder> Reminders,
    IReadOnlyList<TodayHabit> Habits);

/// <param name="Month">0 (Jan) to 11 (Dec).</param>
/// <param name="Left">Revenue minus expenses this month.</param>
public sealed record TodayBudget(int Year, int Month, string MonthName, double Revenue, double Expenses, double Left);

/// <param name="DaysUntilDue">Negative = overdue.</param>
public sealed record TodayPayment(string Id, string Title, double Amount, string DueDate, int DaysUntilDue);

public sealed record TodayReminder(string Id, string Text, DateTimeOffset DueAt, bool Ringing, bool Overdue);

/// <param name="Done">Amount done today (0/1 for check habits).</param>
public sealed record TodayHabit(string Id, string Name, string Icon, string Kind, int Done, int Target, string Unit, bool DoneToday);