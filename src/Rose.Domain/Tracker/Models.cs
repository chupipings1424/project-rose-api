namespace Rose.Domain.Tracker;

/// <summary>A title: one row of the Accounts sheet. Dates are ISO text or "".</summary>
public sealed record Account(
    string Title,
    double TotalAmount,
    string StartDate,
    string DueDate,
    string Frequency,
    int Instalments,
    double PaymentAmount,
    string Notes)
{
    /// <summary><see cref="AccountKind.Plan"/> (a fixed number of instalments) or <see cref="AccountKind.Bill"/>.</summary>
    public string Kind { get; init; } = AccountKind.Plan;

    public bool IsBill => Kind == AccountKind.Bill;
}

/// <summary>
/// What a title tracks. A plan has a total and a fixed number of instalments (a loan, a gadget on
/// installment). A bill never ends and its amount changes every time (water, electricity, WiFi):
/// no total, no instalments - you just log what you paid.
/// </summary>
public static class AccountKind
{
    public const string Plan = "plan";
    public const string Bill = "bill";

    /// <summary>"Bill", " bill " -> "bill"; blank or anything else -> "plan".</summary>
    public static string Normalize(string? kind) =>
        string.Equals(kind?.Trim(), Bill, StringComparison.OrdinalIgnoreCase) ? Bill : Plan;

    public static bool IsKnown(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || string.Equals(kind.Trim(), Bill, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind.Trim(), Plan, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One row of the Payments sheet. Dates are ISO text or ""; time is "9:00am" style text.</summary>
public sealed record Payment(
    string Id,
    string Title,
    double Amount,
    string Date,
    string Time,
    string Status,
    string DueDate,
    string Note)
{
    /// <summary>What this payment added to the budget (null = nothing). Kept so it can be taken back exactly.</summary>
    public BudgetMark? InBudget { get; init; }
}

/// <summary>An amount a paid payment added to a budget month. <paramref name="Month"/> is 0 (Jan) to 11 (Dec).</summary>
public sealed record BudgetMark(int Year, int Month, double Amount);

public static class PaymentStatus
{
    public const string Paid = "Paid";
    public const string Unpaid = "Unpaid";

    public static readonly IReadOnlyList<string> All = [Paid, Unpaid];

    public static bool IsKnown(string? status) => status is Paid or Unpaid;
}

public static class PaymentExtensions
{
    public static bool IsPaid(this Payment payment) => payment.Status == PaymentStatus.Paid;

    /// <summary>When this payment is expected: its due date, else the date it was logged for.</summary>
    public static string DueOrDate(this Payment payment) =>
        payment.DueDate.Length > 0 ? payment.DueDate : payment.Date;

    public static bool HasTitle(this Payment payment, string title) =>
        string.Equals(payment.Title, title, StringComparison.OrdinalIgnoreCase);
}