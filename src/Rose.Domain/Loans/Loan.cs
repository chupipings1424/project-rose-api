namespace Rose.Domain.Loans;

/// <summary>
/// A loan paid by a fixed monthly amortization (Coop, Pag-IBIG, SSS...). Nothing about
/// payments is stored: every due date that has passed counts as paid (salary deduction),
/// so the progress is always worked out from the loan date and today.
/// </summary>
public sealed class Loan
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    /// <summary>One of <see cref="LoanRules.Types"/>.</summary>
    public required string Type { get; set; }

    /// <summary>When the loan was released. Payments fall on this day of each following month.</summary>
    public required DateOnly LoanDate { get; set; }

    /// <summary>Number of monthly payments.</summary>
    public required int Term { get; set; }

    public required decimal MonthlyAmortization { get; set; }

    /// <summary>The amount borrowed (shown for reference; the balance uses amortization x term).</summary>
    public required decimal Amount { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A validated create / edit request.</summary>
public sealed record LoanInput(string Name, string Type, DateOnly LoanDate, int Term, decimal MonthlyAmortization, decimal Amount);

/// <summary>One row of the schedule. <see cref="BalanceAfter"/> is what is left once this payment is made.</summary>
public sealed record LoanInstalment(int Number, DateOnly DueDate, decimal Amount, decimal BalanceAfter, bool Paid);

public static class LoanStatus
{
    /// <summary>Released but the first payment hasn't come yet.</summary>
    public const string NotStarted = "not-started";
    public const string Active = "active";
    public const string Completed = "completed";
}

/// <summary>A loan plus everything calculated from it for "today" - what the API returns.</summary>
public sealed record LoanView(
    string Id,
    string Name,
    string Type,
    DateOnly LoanDate,
    int Term,
    decimal MonthlyAmortization,
    decimal Amount,
    decimal TotalPayable,
    decimal Interest,
    int PaymentsMade,
    int PaymentsLeft,
    decimal Paid,
    decimal Balance,
    double Percent,
    DateOnly? LastPaymentDate,
    DateOnly? NextDueDate,
    DateOnly FinishDate,
    string Status,
    IReadOnlyList<LoanInstalment> Schedule,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);