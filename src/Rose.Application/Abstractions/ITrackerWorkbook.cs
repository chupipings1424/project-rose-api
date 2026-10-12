using Rose.Domain.Budget;
using Rose.Domain.Tracker;

namespace Rose.Application.Abstractions;

/// <summary>
/// Opens payments.xlsx. Only one <see cref="ITrackerWorkbook"/> is open at a time, so two
/// requests can never overwrite each other's changes.
/// </summary>
public interface ITrackerWorkbookStore
{
    /// <summary>Full path of the workbook (shown on the overview page).</summary>
    string WorkbookPath { get; }

    /// <summary>Opens the workbook (creating an empty one the first time). Dispose it to let the next caller in.</summary>
    Task<ITrackerWorkbook> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>The saved file, for downloading.</summary>
    Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A unit of work over the workbook: read, change, then <see cref="Save"/> once.</summary>
public interface ITrackerWorkbook : IDisposable
{
    IReadOnlyList<Account> Accounts();

    /// <summary>Every payment, sorted by date then time.</summary>
    IReadOnlyList<Payment> Payments();

    IReadOnlyList<BudgetLine> BudgetLines();

    void AddAccount(Account account);
    void AddPayment(Payment payment);
    bool SetPaymentStatus(string id, string status);
    bool DeletePayment(string id);

    /// <summary>Deletes the title and its payments. Returns how many payments went, or null if the title is unknown.</summary>
    int? DeleteAccount(string title);

    void AddBudgetLine(BudgetLine line);
    bool SetBudgetAmount(string id, int month, double value);
    bool UpdateBudgetLine(string id, string item, string group, string? notes, double[]? amounts);
    void ReplaceBudgetAmounts(string id, double[] amounts, string? notes);
    bool DeleteBudgetLine(string id);
    int DeleteBudgetYear(int year);

    /// <summary>Writes every change at once (atomically) and refreshes the "Budget YYYY" sheets.</summary>
    void Save();
}