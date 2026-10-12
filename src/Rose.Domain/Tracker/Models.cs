namespace Rose.Domain.Tracker;

/// <summary>A title / instalment plan: one row of the Accounts sheet. Dates are ISO text or "".</summary>
public sealed record Account(
    string Title,
    double TotalAmount,
    string StartDate,
    string DueDate,
    string Frequency,
    int Instalments,
    double PaymentAmount,
    string Notes);

/// <summary>One row of the Payments sheet. Dates are ISO text or ""; time is "9:00am" style text.</summary>
public sealed record Payment(
    string Id,
    string Title,
    double Amount,
    string Date,
    string Time,
    string Status,
    string DueDate,
    string Note);

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