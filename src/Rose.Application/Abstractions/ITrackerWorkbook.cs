using Rose.Domain.Budget;
using Rose.Domain.Tracker;

namespace Rose.Application.Abstractions;

/// <summary>
/// Opens the tracker data (titles, payments, budget). Only one <see cref="ITrackerWorkbook"/> is
/// open at a time, so two requests can never overwrite each other's changes.
/// </summary>
public interface ITrackerWorkbookStore
{
    /// <summary>Where the data lives (shown on the overview page), e.g. "Supabase Postgres".</summary>
    string StorageName { get; }

    /// <summary>Opens a unit of work. Dispose it to let the next caller in.</summary>
    Task<ITrackerWorkbook> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Everything as an Excel workbook, for downloading.</summary>
    Task<byte[]> ExportAsync(CancellationToken cancellationToken = default);
}

/// <summary>A unit of work over the tracker data: read, change, then <see cref="SaveAsync"/> once.</summary>
public interface ITrackerWorkbook : IDisposable
{
    IReadOnlyList<Account> Accounts();

    /// <summary>Every payment, sorted by date then time.</summary>
    IReadOnlyList<Payment> Payments();

    IReadOnlyList<BudgetLine> BudgetLines();

    void AddAccount(Account account);
    void AddPayment(Payment payment);

    /// <summary>Overwrites the row whose id matches <paramref name="payment"/>. Returns false if there is none.</summary>
    bool UpdatePayment(Payment payment);

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

    /// <summary>Writes every change at once, in one transaction. Disposing without saving discards them.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}