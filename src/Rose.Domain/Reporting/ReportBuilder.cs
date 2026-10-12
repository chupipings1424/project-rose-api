using System.Globalization;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Domain.Reporting;

/// <summary>Read-only aggregation of payments + budget for the report page.</summary>
public static class ReportBuilder
{
    public static ReportSummary Build(
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<BudgetLine> budgetLines,
        string? requestedYear,
        DateTime localNow,
        string workbookPath)
    {
        var today = DateOnly.FromDateTime(localNow);
        var todayIso = Coerce.IsoDate(today);
        var currentYear = localNow.Year.ToString(CultureInfo.InvariantCulture);

        var allYears = BudgetView.YearsOf(budgetLines, localNow.Year)
            .Select(y => y.ToString(CultureInfo.InvariantCulture))
            .Concat(payments.Where(p => p.Date.Length > 0).Select(p => p.Date[..4]))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        var year = !string.IsNullOrEmpty(requestedYear) && allYears.Contains(requestedYear)
            ? requestedYear
            : allYears.Contains(currentYear) ? currentYear : allYears.Count > 0 ? allYears[^1] : currentYear;
        var prefix = $"{year}-";

        // ---- payments ----
        var paid = payments.Where(p => p.IsPaid()).ToList();
        var unpaid = payments.Where(p => !p.IsPaid()).ToList();
        var paidTotal = Coerce.Round(paid.Sum(p => p.Amount));
        var unpaidTotal = Coerce.Round(unpaid.Sum(p => p.Amount));
        var trackedTotal = Coerce.Round(accounts.Sum(a => a.TotalAmount));
        var scheduledTotal = Coerce.Round(paidTotal + unpaidTotal);
        var planTotal = trackedTotal != 0 ? trackedTotal : scheduledTotal;

        var overdue = unpaid.Where(p => p.DueOrDate().Length > 0 && string.CompareOrdinal(p.DueOrDate(), todayIso) < 0).ToList();

        var upcoming = unpaid
            .Where(p => p.DueOrDate().Length > 0)
            .Select(p => new UpcomingPayment(
                p.Title,
                p.Amount,
                p.DueOrDate(),
                (Coerce.ToDate(p.DueOrDate()) ?? today).DayNumber - today.DayNumber))
            .OrderBy(u => u.DueDate, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        var timeline = new List<TimelinePoint>();
        var running = 0.0;
        foreach (var p in payments.OrderBy(p => p.Date, StringComparer.Ordinal).ThenBy(p => p.Time, StringComparer.Ordinal))
        {
            if (p.Date.Length == 0) continue;
            if (p.IsPaid()) running += p.Amount;
            timeline.Add(new TimelinePoint(p.Date, p.Title, Coerce.Round(p.Amount), p.Status, Coerce.Round(running)));
        }

        var activityPaid = new double[12];
        var activityScheduled = new double[12];
        foreach (var p in payments)
        {
            var when = p.Date.Length > 0 ? p.Date : p.DueDate;
            if (when.Length < 7 || !when.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(when.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                || month is < 1 or > 12) continue;
            if (p.IsPaid()) activityPaid[month - 1] += p.Amount;
            else activityScheduled[month - 1] += p.Amount;
        }
        var activity = BudgetRules.Months
            .Select((name, i) => new MonthActivity(name, Coerce.Round(activityPaid[i]), Coerce.Round(activityScheduled[i])))
            .ToList();

        var byTitle = BuildTitleProgress(accounts, payments);

        // ---- budget ----
        var book = BudgetView.Build(int.Parse(year, CultureInfo.InvariantCulture), budgetLines, localNow.Year);
        var revenue = book.Section(BudgetRules.Revenue);
        var expense = book.Section(BudgetRules.Expense);
        var cumulativeNet = new double[12];
        var runningNet = 0.0;
        for (var m = 0; m < 12; m++)
        {
            runningNet += book.NetByMonth[m];
            cumulativeNet[m] = Coerce.Round(runningNet);
        }

        var hasPaymentData = payments.Count > 0 || accounts.Count > 0;
        var hasBudgetData = book.LineCount > 0;

        return new ReportSummary(
            GeneratedAt: localNow.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            Workbook: workbookPath,
            Year: book.Year,
            Years: allYears.Select(y => int.Parse(y, CultureInfo.InvariantCulture)).ToList(),
            HasData: hasPaymentData || hasBudgetData,
            HasPaymentData: hasPaymentData,
            HasBudgetData: hasBudgetData,
            Months: BudgetRules.Months,
            Kpis: new ReportKpis(
                TrackedTotal: planTotal,
                PaidTotal: paidTotal,
                Outstanding: Coerce.Round(Math.Max(planTotal - paidTotal, 0)),
                Progress: planTotal != 0 ? Coerce.Round(paidTotal / planTotal * 100, 1) : 0,
                Accounts: accounts.Count,
                PaymentCount: payments.Count,
                PaidCount: paid.Count,
                UnpaidCount: unpaid.Count,
                OverdueCount: overdue.Count,
                OverdueTotal: Coerce.Round(overdue.Sum(p => p.Amount)),
                NextDue: upcoming.Count > 0 ? upcoming[0].DueDate : null,
                DaysUntilDue: upcoming.Count > 0 ? upcoming[0].Days : null,
                BudgetRevenue: book.TotalRevenue,
                BudgetExpenses: book.TotalExpenses,
                BudgetNet: book.Net,
                BudgetLines: book.LineCount),
            Payments: new ReportPayments(
                timeline,
                activity,
                byTitle,
                upcoming,
                [
                    new StatusSlice("Paid", paidTotal, paid.Count),
                    new StatusSlice("Scheduled", unpaidTotal, unpaid.Count),
                    new StatusSlice("Not yet scheduled", Coerce.Round(Math.Max(planTotal - scheduledTotal, 0)), 0),
                ]),
            Budget: new ReportBudget(
                revenue.Monthly,
                expense.Monthly,
                book.NetByMonth,
                cumulativeNet,
                GroupsOf(revenue),
                GroupsOf(expense),
                ItemsOf(expense),
                ItemsOf(revenue)));
    }

    private static List<TitleProgress> BuildTitleProgress(IReadOnlyList<Account> accounts, IReadOnlyList<Payment> payments)
    {
        var rows = new List<TitleProgress>();
        foreach (var account in accounts)
        {
            var mine = payments.Where(p => p.HasTitle(account.Title)).ToList();
            var done = Coerce.Round(mine.Where(p => p.IsPaid()).Sum(p => p.Amount));
            var total = account.TotalAmount != 0 ? account.TotalAmount : Coerce.Round(mine.Sum(p => p.Amount));
            rows.Add(new TitleProgress(
                account.Title,
                Coerce.Round(total),
                done,
                Coerce.Round(Math.Max(total - done, 0)),
                total != 0 ? Coerce.Round(done / total * 100, 1) : 0,
                mine.Count(p => p.IsPaid()),
                mine.Count,
                account.Frequency,
                account.PaymentAmount));
        }

        // Titles that only exist on the Payments sheet ("loose" titles).
        var known = accounts.Select(a => a.Title.ToLowerInvariant()).ToHashSet();
        var loose = new Dictionary<string, (string Title, double Total, double Paid, int PaidCount, int Count)>();
        var order = new List<string>();
        foreach (var p in payments)
        {
            var key = p.Title.ToLowerInvariant();
            if (known.Contains(key)) continue;
            if (!loose.TryGetValue(key, out var entry))
            {
                entry = (p.Title, 0, 0, 0, 0);
                order.Add(key);
            }
            entry.Total += p.Amount;
            entry.Count++;
            if (p.IsPaid())
            {
                entry.Paid += p.Amount;
                entry.PaidCount++;
            }
            loose[key] = entry;
        }
        foreach (var key in order)
        {
            var e = loose[key];
            var total = Coerce.Round(e.Total);
            var paidSum = Coerce.Round(e.Paid);
            rows.Add(new TitleProgress(
                e.Title, total, paidSum, Coerce.Round(Math.Max(total - paidSum, 0)),
                total != 0 ? Coerce.Round(paidSum / total * 100, 1) : 0,
                e.PaidCount, e.Count, "", 0));
        }

        return rows.OrderByDescending(r => r.Total).ToList();
    }

    private static List<NamedTotal> GroupsOf(BudgetSectionView section) =>
        section.Groups
            .Where(g => g.Total != 0)
            .Select(g => new NamedTotal(g.Name.Length > 0 ? g.Name : "Uncategorised", g.Total, g.Monthly))
            .OrderByDescending(g => g.Total)
            .ToList();

    private static List<TopItem> ItemsOf(BudgetSectionView section, int limit = 8) =>
        section.Groups
            .SelectMany(g => g.Lines.Where(l => l.Total != 0)
                .Select(l => new TopItem(l.Item, g.Name.Length > 0 ? g.Name : "Uncategorised", l.Total)))
            .OrderByDescending(i => i.Total)
            .Take(limit)
            .ToList();
}