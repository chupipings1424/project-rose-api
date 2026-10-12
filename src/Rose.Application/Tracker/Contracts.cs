using Rose.Domain.Budget;
using Rose.Domain.Tracker;

namespace Rose.Application.Tracker;

// Requests are deliberately lenient: amounts and counts may arrive as numbers or text
// ("1,200.50"), exactly like the original Flask API accepted them.

public sealed record CreateAccountRequest(
    string? Title,
    string? Amount,
    string? TotalAmount,
    string? Instalments,
    string? Frequency,
    string? StartDate,
    string? DueDate,
    string? AmountMode,
    string? Notes);

public sealed record SchedulePreviewRequest(
    string? Amount, string? TotalAmount, string? Instalments, string? Frequency, string? StartDate, string? AmountMode);

public sealed record AddPaymentRequest(
    string? Title, string? Amount, string? Date, string? Time, string? Status, string? DueDate, string? Note);

public sealed record UpdatePaymentRequest(string? Status, string? Title);

public sealed record AddBudgetLineRequest(
    string? Section, string? Group, string? Item, string? Year, List<string?>? Amounts, string? Notes);

/// <summary>Send <c>month</c> (+ <c>value</c>) to change one cell, otherwise item / group / notes / amounts.</summary>
public sealed record UpdateBudgetLineRequest(
    string? Month, string? Value, string? Item, string? Group, string? Notes, List<string?>? Amounts, string? Year);

public sealed record CopyBudgetYearRequest(string? From, string? To, bool IncludeAmounts);

public sealed record HomeStats(string Workbook, HomePayments Payments, HomeBudget Budget);

public sealed record HomePayments(int Accounts, double Total, int PaidCount, int PaymentCount, string? NextDue);

public sealed record HomeBudget(int Year, double Revenue, double Expenses, double Net, int LineCount);

public sealed record ServerNow(string Date, string Time);

public sealed record OkResponse(bool Ok = true);

public sealed record PaymentCreated(string Id, AccountSummary Summary);

public sealed record PaymentUpdated(bool Ok, AccountSummary Summary);

public sealed record AccountDeleted(bool Ok, int DeletedPayments, IReadOnlyList<Account> Accounts);

public sealed record BudgetLineCreated(string Id, BudgetView Budget);

public sealed record BudgetChanged(bool Ok, BudgetView Budget);

public sealed record BudgetYearDeleted(bool Ok, int Deleted, BudgetView Budget);

public sealed record BudgetYearCopied(int Copied, BudgetView Budget);