using Rose.Domain.Common;
using Rose.Domain.Loans;

namespace Rose.UnitTests.Domain;

public sealed class LoanRulesTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Loan Coop(string loanDate = "2026-04-02", int term = 24, decimal monthly = 500, decimal amount = 10_000) => new()
    {
        Id = "l1",
        Name = "Coop",
        Type = "Coop",
        LoanDate = DateOnly.Parse(loanDate),
        Term = term,
        MonthlyAmortization = monthly,
        Amount = amount,
        CreatedAt = Stamp,
        UpdatedAt = Stamp,
    };

    [Theory]
    [InlineData("2026-04-02", 0)] // the loan date itself is not a payment
    [InlineData("2026-05-01", 0)]
    [InlineData("2026-05-02", 1)] // the first payment is one month later, same day
    [InlineData("2026-11-01", 6)]
    [InlineData("2026-11-02", 7)]
    [InlineData("2028-04-02", 24)]
    [InlineData("2035-01-01", 24)] // never more than the term
    [InlineData("2025-12-31", 0)] // before the loan date
    public void A_payment_is_made_on_each_monthly_due_date(string today, int expected) =>
        Assert.Equal(expected, LoanRules.PaymentsMade(new DateOnly(2026, 4, 2), 24, DateOnly.Parse(today)));

    [Fact]
    public void A_loan_on_the_31st_pays_on_the_last_day_of_short_months()
    {
        var loanDate = new DateOnly(2026, 1, 31);

        Assert.Equal(new DateOnly(2026, 2, 28), LoanRules.DueDate(loanDate, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), LoanRules.DueDate(loanDate, 2));
        Assert.Equal(new DateOnly(2026, 4, 30), LoanRules.DueDate(loanDate, 3));
        Assert.Equal(1, LoanRules.PaymentsMade(loanDate, 12, new DateOnly(2026, 2, 28)));
        Assert.Equal(1, LoanRules.PaymentsMade(loanDate, 12, new DateOnly(2026, 3, 30)));
        Assert.Equal(2, LoanRules.PaymentsMade(loanDate, 12, new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void Summary_shows_paid_balance_and_dates()
    {
        var view = LoanRules.Summarize(Coop(), new DateOnly(2026, 11, 11));

        Assert.Equal(12_000m, view.TotalPayable);
        Assert.Equal(2_000m, view.Interest);
        Assert.Equal(7, view.PaymentsMade);
        Assert.Equal(17, view.PaymentsLeft);
        Assert.Equal(3_500m, view.Paid);
        Assert.Equal(8_500m, view.Balance);
        Assert.Equal(29.2, view.Percent);
        Assert.Equal(new DateOnly(2026, 11, 2), view.LastPaymentDate);
        Assert.Equal(new DateOnly(2026, 12, 2), view.NextDueDate);
        Assert.Equal(new DateOnly(2028, 4, 2), view.FinishDate);
        Assert.Equal(LoanStatus.Active, view.Status);
    }

    [Fact]
    public void Schedule_lists_every_payment_with_the_balance_after_it()
    {
        var schedule = LoanRules.Summarize(Coop(term: 3, monthly: 1000, amount: 3000), new DateOnly(2026, 5, 2)).Schedule;

        Assert.Equal(
            [
                new LoanInstalment(1, new DateOnly(2026, 5, 2), 1000, 2000, true),
                new LoanInstalment(2, new DateOnly(2026, 6, 2), 1000, 1000, false),
                new LoanInstalment(3, new DateOnly(2026, 7, 2), 1000, 0, false),
            ],
            schedule);
    }

    [Fact]
    public void A_finished_loan_is_completed_with_nothing_left()
    {
        var view = LoanRules.Summarize(Coop(loanDate: "2020-01-15", term: 12), new DateOnly(2026, 11, 11));

        Assert.Equal(LoanStatus.Completed, view.Status);
        Assert.Equal(0m, view.Balance);
        Assert.Equal(100, view.Percent);
        Assert.Null(view.NextDueDate);
        Assert.Equal(new DateOnly(2021, 1, 15), view.FinishDate);
    }

    [Fact]
    public void A_new_loan_has_not_started_yet()
    {
        var view = LoanRules.Summarize(Coop(), new DateOnly(2026, 4, 10));

        Assert.Equal(LoanStatus.NotStarted, view.Status);
        Assert.Null(view.LastPaymentDate);
        Assert.Equal(new DateOnly(2026, 5, 2), view.NextDueDate);
        Assert.Equal(12_000m, view.Balance);
    }

    [Fact]
    public void Interest_is_never_negative()
    {
        var view = LoanRules.Summarize(Coop(term: 10, monthly: 900, amount: 10_000), new DateOnly(2026, 4, 2));
        Assert.Equal(0m, view.Interest);
    }

    [Fact]
    public void Parse_cleans_a_valid_request()
    {
        var input = LoanRules.Parse("  Pag-IBIG MPL ", "pag-ibig", "2025-03-15", 24, 2_345.678m, 50_000);

        Assert.Equal(new LoanInput("Pag-IBIG MPL", "Pag-IBIG", new DateOnly(2025, 3, 15), 24, 2_345.68m, 50_000), input);
        Assert.Equal("Other", LoanRules.Parse("x", null, "2025-03-15", 1, 1, 1).Type);
    }

    [Theory]
    [InlineData("", "Coop", "2026-04-02", 24, 500, 10000, "Give the loan a name.")]
    [InlineData("Coop", "Casino", "2026-04-02", 24, 500, 10000, "Choose a loan type")]
    [InlineData("Coop", "Coop", "", 24, 500, 10000, "Enter the loan date.")]
    [InlineData("Coop", "Coop", "02/04/2026", 24, 500, 10000, "Pick a valid loan date.")]
    [InlineData("Coop", "Coop", "2026-04-02", 0, 500, 10000, "The term must be at least 1 month.")]
    [InlineData("Coop", "Coop", "2026-04-02", 601, 500, 10000, "The term can be at most 600 months.")]
    [InlineData("Coop", "Coop", "2026-04-02", 24, 0, 10000, "The monthly amortization must be more than zero.")]
    [InlineData("Coop", "Coop", "2026-04-02", 24, 500, -1, "The loan amount must be more than zero.")]
    public void Parse_rejects_bad_input(string name, string type, string date, int term, double monthly, double amount, string error)
    {
        var ex = Assert.Throws<ValidationException>(() => LoanRules.Parse(name, type, date, term, (decimal)monthly, (decimal)amount));
        Assert.StartsWith(error, ex.Message);
    }

    [Fact]
    public void Parse_requires_term_and_amounts() =>
        Assert.Throws<ValidationException>(() => LoanRules.Parse("Coop", "Coop", "2026-04-02", null, 500, 10000));
}