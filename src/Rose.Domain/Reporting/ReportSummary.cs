namespace Rose.Domain.Reporting;

public sealed record ReportSummary(
    string GeneratedAt,
    string Workbook,
    int Year,
    IReadOnlyList<int> Years,
    bool HasData,
    bool HasPaymentData,
    bool HasBudgetData,
    IReadOnlyList<string> Months,
    ReportKpis Kpis,
    ReportPayments Payments,
    ReportBudget Budget);

public sealed record ReportKpis(
    double TrackedTotal,
    double PaidTotal,
    double Outstanding,
    double Progress,
    int Accounts,
    int PaymentCount,
    int PaidCount,
    int UnpaidCount,
    int OverdueCount,
    double OverdueTotal,
    string? NextDue,
    int? DaysUntilDue,
    double BudgetRevenue,
    double BudgetExpenses,
    double BudgetNet,
    int BudgetLines);

public sealed record ReportPayments(
    IReadOnlyList<TimelinePoint> Timeline,
    IReadOnlyList<MonthActivity> Activity,
    IReadOnlyList<TitleProgress> ByTitle,
    IReadOnlyList<UpcomingPayment> Upcoming,
    IReadOnlyList<StatusSlice> StatusSplit);

public sealed record TimelinePoint(string Date, string Title, double Amount, string Status, double CumulativePaid);

public sealed record MonthActivity(string Month, double Paid, double Scheduled);

public sealed record TitleProgress(
    string Title,
    double Total,
    double Paid,
    double Remaining,
    double Progress,
    int PaidCount,
    int PaymentCount,
    string Frequency,
    double PerPayment);

public sealed record UpcomingPayment(string Title, double Amount, string DueDate, int Days);

public sealed record StatusSlice(string Label, double Value, int Count);

public sealed record ReportBudget(
    double[] RevenueMonthly,
    double[] ExpenseMonthly,
    double[] NetByMonth,
    double[] CumulativeNet,
    IReadOnlyList<NamedTotal> RevenueGroups,
    IReadOnlyList<NamedTotal> ExpenseGroups,
    IReadOnlyList<TopItem> TopExpenseItems,
    IReadOnlyList<TopItem> TopRevenueItems);

public sealed record NamedTotal(string Name, double Total, double[] Monthly);

public sealed record TopItem(string Item, string Group, double Total);

