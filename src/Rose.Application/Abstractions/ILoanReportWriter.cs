using Rose.Domain.Loans;

namespace Rose.Application.Abstractions;

/// <summary>Turns the loans into a spreadsheet (an .xlsx file's bytes).</summary>
public interface ILoanReportWriter
{
    byte[] Write(IReadOnlyList<LoanView> loans, DateOnly asOf);
}