namespace Rose.Application.Loans;

/// <summary>Create or edit a loan. <c>loan_date</c> is <c>yyyy-MM-dd</c>; <c>type</c> is one of the loan types (default "Other").</summary>
public sealed record LoanRequest(string? Name, string? Type, string? LoanDate, int? Term, decimal? MonthlyAmortization, decimal? Amount);