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
    IReadOnlyList<Payment> Payments,
    string Kind,
    double PaidThisMonth,
    double PaidThisYear,
    double AveragePerMonth,
    IReadOnlyList<MonthTotal> Monthly)
{
    /// <summary>How many months <see cref="Monthly"/> covers (ending with the current month).</summary>
    public const int MonthsShown = 12;

    /// <param name="title">The title to summarise (matched case-insensitively).</param>
    /// <param name="accounts">Every account in the workbook.</param>
    /// <param name="allPayments">Every payment in the workbook, already sorted by date and time.</param>
    /// <param name="today">Today's local date.</param>
    public static AccountSummary For(
        string title, IEnumerable<Account> accounts, IEnumerable<Payment> allPayments, DateOnly today)
    {
        var account = accounts.FirstOrDefault(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase));
        var isBill = account?.IsBill == true;
        var payments = allPayments.Where(p => p.HasTitle(title)).ToList();
        var unpaidPayments = payments.Where(p => !p.IsPaid()).ToList();

        var paid = payments.Where(p => p.IsPaid()).Sum(p => p.Amount);
        var unpaid = unpaidPayments.Sum(p => p.Amount);
        // A bill has no total: "total" is what was paid, and only bills logged as Unpaid remain.
        var totalAmount = isBill ? paid : account?.TotalAmount ?? 0;
        var remaining = isBill ? Coerce.Round(unpaid) : Coerce.Round(totalAmount - paid);

        // Earliest unpaid due date, else the account's own due date.
        var unpaidDues = unpaidPayments.Select(p => p.DueOrDate()).Where(d => d.Length > 0).Order(StringComparer.Ordinal).ToList();
        var nextDue = unpaidDues.Count > 0 ? unpaidDues[0] : account?.DueDate ?? "";
        int? daysLeft = Coerce.ToDate(nextDue) is { } due ? due.DayNumber - today.DayNumber : null;

        var progress = !isBill && totalAmount != 0 ? Coerce.Round(paid / totalAmount * 100, 1) : 0;
        var instalments = account?.Instalments ?? 0;
        var perPayment = account?.PaymentAmount ?? 0;

        var monthly = MonthlyPaid(payments, today);
        var paidMonths = monthly.Where(m => m.Amount > 0).ToList();

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
            HasPlan: !isBill && instalments != 0 && perPayment != 0,
            Frequency: account?.Frequency ?? "",
            Instalments: instalments,
            PerPayment: Coerce.Round(perPayment),
            PaymentsLeft: unpaidPayments.Count,
            ProjectedPayoffDate: unpaidDues.Count > 0 ? unpaidDues[^1] : "",
            Payments: payments,
            Kind: account?.Kind ?? AccountKind.Plan,
            PaidThisMonth: monthly[^1].Amount,
            PaidThisYear: Coerce.Round(payments
                .Where(p => p.IsPaid() && PaidOn(p) is { } day && day.Year == today.Year)
                .Sum(p => p.Amount)),
            AveragePerMonth: paidMonths.Count > 0 ? Coerce.Round(paidMonths.Average(m => m.Amount)) : 0,
            Monthly: monthly);
    }

    /// <summary>Paid amounts per month for the last <see cref="MonthsShown"/> months, oldest first (months with nothing are 0).</summary>
    private static List<MonthTotal> MonthlyPaid(IReadOnlyList<Payment> payments, DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(MonthsShown - 1));
        var totals = new double[MonthsShown];
        foreach (var payment in payments.Where(p => p.IsPaid()))
        {
            if (PaidOn(payment) is not { } day) continue;
            var index = (day.Year - first.Year) * 12 + day.Month - first.Month;
            if (index is >= 0 and < MonthsShown) totals[index] += payment.Amount;
        }
        return [.. totals.Select((amount, i) =>
        {
            var month = first.AddMonths(i);
            return new MonthTotal(Coerce.IsoDate(month)[..7], Coerce.Round(amount));
        })];
    }

    /// <summary>The day a payment counts for: the date it was paid, else its due date.</summary>
    private static DateOnly? PaidOn(Payment payment) =>
        Coerce.ToDate(payment.Date) ?? Coerce.ToDate(payment.DueDate);
}

/// <summary>What was paid in one month. <paramref name="Month"/> is "yyyy-MM".</summary>
public sealed record MonthTotal(string Month, double Amount);