using System.Globalization;
using Rose.Domain.Common;

namespace Rose.Domain.Loans;

/// <summary>
/// The loan maths. Pure functions: "today" is always passed in, so the tests can jump to
/// any date. A payment is "made" as soon as its due date arrives (salary deduction).
/// </summary>
public static class LoanRules
{
    public const int MaxTerm = 600;
    public const decimal MaxAmount = 1_000_000_000_000m;
    public const string OtherType = "Other";

    public static readonly IReadOnlyList<string> Types =
        ["Coop", "Pag-IBIG", "SSS", "GSIS", "Bank", "Salary loan", "Personal", OtherType];

    private static readonly DateOnly Earliest = new(1950, 1, 1);
    private static readonly DateOnly Latest = new(2100, 12, 31);

    /// <summary>
    /// The due date of payment <paramref name="number"/> (1 = the first): the loan date's day,
    /// that many months later. A loan on the 31st falls on the last day of shorter months
    /// (Feb 28) and back on the 31st afterwards, because it is always counted from the loan date.
    /// </summary>
    public static DateOnly DueDate(DateOnly loanDate, int number) => loanDate.AddMonths(number);

    /// <summary>How many due dates are on or before <paramref name="today"/> (0 to the term).</summary>
    public static int PaymentsMade(DateOnly loanDate, int term, DateOnly today)
    {
        if (today <= loanDate) return 0;
        var months = (today.Year - loanDate.Year) * 12 + today.Month - loanDate.Month;
        if (months > 0 && DueDate(loanDate, months) > today) months--;
        return Math.Clamp(months, 0, term);
    }

    /// <summary>Everything the page shows for one loan.</summary>
    public static LoanView Summarize(Loan loan, DateOnly today)
    {
        var total = loan.MonthlyAmortization * loan.Term;
        var made = PaymentsMade(loan.LoanDate, loan.Term, today);
        var paid = loan.MonthlyAmortization * made;
        var schedule = Enumerable.Range(1, loan.Term)
            .Select(n => new LoanInstalment(n, DueDate(loan.LoanDate, n), loan.MonthlyAmortization, total - loan.MonthlyAmortization * n, n <= made))
            .ToList();
        var status = made >= loan.Term ? LoanStatus.Completed : made == 0 ? LoanStatus.NotStarted : LoanStatus.Active;

        return new LoanView(
            loan.Id,
            loan.Name,
            loan.Type,
            loan.LoanDate,
            loan.Term,
            loan.MonthlyAmortization,
            loan.Amount,
            total,
            Math.Max(0, total - loan.Amount),
            made,
            loan.Term - made,
            paid,
            total - paid,
            Math.Round(made * 100.0 / loan.Term, 1),
            made > 0 ? DueDate(loan.LoanDate, made) : null,
            made < loan.Term ? DueDate(loan.LoanDate, made + 1) : null,
            DueDate(loan.LoanDate, loan.Term),
            status,
            schedule,
            loan.CreatedAt,
            loan.UpdatedAt);
    }

    /// <summary>Validates an untrusted create / edit request.</summary>
    public static LoanInput Parse(string? name, string? type, string? loanDate, int? term, decimal? monthlyAmortization, decimal? amount)
    {
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length == 0) throw new ValidationException("Give the loan a name.");
        if (cleanName.Length > 60) throw new ValidationException("Keep the name under 60 characters.");

        var cleanType = string.IsNullOrWhiteSpace(type)
            ? OtherType
            : Types.FirstOrDefault(t => string.Equals(t, type.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Choose a loan type: {string.Join(", ", Types)}.");

        if (string.IsNullOrWhiteSpace(loanDate)) throw new ValidationException("Enter the loan date.");
        if (!DateOnly.TryParseExact(loanDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || day < Earliest || day > Latest)
            throw new ValidationException("Pick a valid loan date.");

        if (term is not { } months || months < 1) throw new ValidationException("The term must be at least 1 month.");
        if (months > MaxTerm) throw new ValidationException($"The term can be at most {MaxTerm} months.");

        return new LoanInput(cleanName, cleanType, day, months, Money(monthlyAmortization, "monthly amortization"), Money(amount, "loan amount"));
    }

    private static decimal Money(decimal? value, string what)
    {
        if (value is not { } amount || amount <= 0) throw new ValidationException($"The {what} must be more than zero.");
        if (amount > MaxAmount) throw new ValidationException($"That {what} is too large.");
        return Math.Round(amount, 2);
    }
}