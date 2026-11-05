using Rose.Application.Abstractions;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Application.Tracker;

/// <summary>
/// Keeps the budget in step with the Payment Tracker: every Paid payment adds its amount to the
/// Expense line named like its title (in the month of its date), creating that line under
/// <see cref="BudgetRules.LoansGroup"/> when there is none. Each payment remembers what it added
/// (<see cref="Payment.InBudget"/>), so unpaying or deleting it takes back exactly that amount -
/// even if the budget line was edited by hand in between.
/// </summary>
public static class PaymentBudgetSync
{
    private const string NewLineNote = "Paid payments from the Payment Tracker";

    /// <summary>
    /// Brings every payment's budget share up to date. Returns how many payments changed (0 = nothing to save).
    /// The first run after this feature shipped adds the payments that were already Paid.
    /// </summary>
    public static int Reconcile(ITrackerWorkbook book, DateOnly today)
    {
        var changed = 0;
        foreach (var payment in book.Payments())
        {
            var wanted = Wanted(payment, today);
            if (payment.InBudget == wanted) continue;
            if (payment.InBudget is { } old) Adjust(book, payment.Title, old, -old.Amount);
            if (wanted is not null) Adjust(book, payment.Title, wanted, wanted.Amount);
            book.SetPaymentBudget(payment.Id, wanted);
            changed++;
        }
        return changed;
    }

    /// <summary>For reads: reconciles and saves only when something changed (e.g. the first visit after an upgrade).</summary>
    public static async Task SyncAsync(ITrackerWorkbook book, DateOnly today, CancellationToken ct)
    {
        if (Reconcile(book, today) > 0) await book.SaveAsync(ct);
    }

    /// <summary>Takes back what these payments added, before they are deleted.</summary>
    public static void Release(ITrackerWorkbook book, IEnumerable<Payment> payments)
    {
        foreach (var payment in payments)
        {
            if (payment.InBudget is not { } mark) continue;
            Adjust(book, payment.Title, mark, -mark.Amount);
            book.SetPaymentBudget(payment.Id, null);
        }
    }

    /// <summary>Marks payments as already counted without touching the budget (an import brings its own budget).</summary>
    public static void MarkCounted(ITrackerWorkbook book, IEnumerable<Payment> payments, DateOnly today)
    {
        foreach (var payment in payments) book.SetPaymentBudget(payment.Id, Wanted(payment, today));
    }

    /// <summary>What a payment should have added: its amount in its month if it is Paid, else nothing.</summary>
    public static BudgetMark? Wanted(Payment payment, DateOnly today)
    {
        if (!payment.IsPaid() || payment.Amount <= 0 || payment.Title.Trim().Length == 0) return null;
        var day = Coerce.ToDate(payment.Date) ?? Coerce.ToDate(payment.DueDate) ?? today;
        return new BudgetMark(day.Year, day.Month - 1, Coerce.Round(payment.Amount));
    }

    private static void Adjust(ITrackerWorkbook book, string title, BudgetMark mark, double delta)
    {
        var item = title.Trim();
        var candidates = book.BudgetLines()
            .Where(l => l.Year == mark.Year
                && l.Section == BudgetRules.Expense
                && string.Equals(l.Item, item, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var line = candidates.Find(l => string.Equals(l.Group, BudgetRules.LoansGroup, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();

        if (line is not null)
        {
            // Never below zero: the line may have been lowered by hand since.
            book.SetBudgetAmount(line.Id, mark.Month, Math.Max(0, Coerce.Round(line.Amounts[mark.Month] + delta)));
            return;
        }
        if (delta <= 0) return; // the line was deleted or renamed by hand - nothing left to take back

        var amounts = new double[12];
        amounts[mark.Month] = Coerce.Round(delta);
        book.AddBudgetLine(new BudgetLine(
            PaymentTrackerService.NewId(), mark.Year, BudgetRules.Expense, BudgetRules.LoansGroup, item,
            amounts, amounts[mark.Month], NewLineNote));
    }
}