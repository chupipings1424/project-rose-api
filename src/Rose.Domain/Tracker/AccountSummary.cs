using Rose.Domain.Common;

namespace Rose.Domain.Tracker;

/// <summary>Everything the detail view shows for one title.</summary>
public sealed record AccountSummary(
    string Title,
    Account? Account,
    double TotalAmount,
    double TotalPaid,
    double TotalUnpaid,
    double Remaining,
    double Progress,
    int PaymentCount,
    int PaidCount,
    int UnpaidCount,
    string NextDueDate,
    int? DaysUntilDue,
    bool Overdue,
    Payment? LastPayment,
    bool HasPlan,
    string Frequency,
    int Instalments,
    double PerPayment,
    int PaymentsLeft,
    string ProjectedPayoffDate,
    IReadOnlyList<Payment> Payments)
{
    /// <param name="title">The title to summarise (matched case-insensitively).</param>
    /// <param name="accounts">Every account in the workbook.</param>
    /// <param name="allPayments">Every payment in the workbook, already sorted by date and time.</param>
    /// <param name="today">Today's local date.</param>
    public static AccountSummary For(
        string title, IEnumerable<Account> accounts, IEnumerable<Payment> allPayments, DateOnly today)
    {
        var account = accounts.FirstOrDefault(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase));
        var payments = allPayments.Where(p => p.HasTitle(title)).ToList();
        var unpaidPayments = payments.Where(p => !p.IsPaid()).ToList();

        var totalAmount = account?.TotalAmount ?? 0;
        var paid = payments.Where(p => p.IsPaid()).Sum(p => p.Amount);
        var unpaid = unpaidPayments.Sum(p => p.Amount);
        var remaining = Coerce.Round(totalAmount - paid);

        // Earliest unpaid due date, else the account's own due date.
        var unpaidDues = unpaidPayments.Select(p => p.DueOrDate()).Where(d => d.Length > 0).Order(StringComparer.Ordinal).ToList();
        var nextDue = unpaidDues.Count > 0 ? unpaidDues[0] : account?.DueDate ?? "";
        int? daysLeft = Coerce.ToDate(nextDue) is { } due ? due.DayNumber - today.DayNumber : null;

        var progress = totalAmount != 0 ? Coerce.Round(paid / totalAmount * 100, 1) : 0;
        var instalments = account?.Instalments ?? 0;
        var perPayment = account?.PaymentAmount ?? 0;

        return new AccountSummary(
            Title: account?.Title ?? title,
            Account: account,
            TotalAmount: Coerce.Round(totalAmount),
            TotalPaid: Coerce.Round(paid),
            TotalUnpaid: Coerce.Round(unpaid),
            Remaining: remaining,
            Progress: Math.Min(progress, 100),
            PaymentCount: payments.Count,
            PaidCount: payments.Count(p => p.IsPaid()),
            UnpaidCount: unpaidPayments.Count,
            NextDueDate: nextDue,
            DaysUntilDue: daysLeft,
            Overdue: daysLeft < 0 && remaining > 0,
            LastPayment: payments.LastOrDefault(p => p.IsPaid()),
            HasPlan: instalments != 0 && perPayment != 0,
            Frequency: account?.Frequency ?? "",
            Instalments: instalments,
            PerPayment: Coerce.Round(perPayment),
            PaymentsLeft: unpaidPayments.Count,
            ProjectedPayoffDate: unpaidDues.Count > 0 ? unpaidDues[^1] : "",
            Payments: payments);
    }
}