using Microsoft.EntityFrameworkCore;
using Rose.Application.Abstractions;
using Rose.Infrastructure.Excel;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// The payment tracker and budget in PostgreSQL. One session at a time (a semaphore), so two
/// requests can never interleave their read-change-save cycles.
/// </summary>
public sealed class PostgresTrackerStore(IDbContextFactory<RoseDbContext> contexts, TimeProvider clock)
    : ITrackerWorkbookStore, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public string StorageName => "Supabase Postgres";

    public async Task<ITrackerWorkbook> OpenAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        RoseDbContext? db = null;
        try
        {
            db = await contexts.CreateDbContextAsync(cancellationToken);
            var accounts = await db.Accounts.OrderBy(a => a.Seq).ToListAsync(cancellationToken);
            var payments = await db.Payments.OrderBy(p => p.Seq).ToListAsync(cancellationToken);
            var lines = await db.BudgetLines.OrderBy(l => l.Seq).ToListAsync(cancellationToken);
            return new PostgresTrackerBook(db, accounts, payments, lines, () => gate.Release());
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            gate.Release();
            throw;
        }
    }

    /// <summary>Everything as a payments.xlsx (Accounts, Payments, Budget and the yearly report sheets).</summary>
    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        using var book = await OpenAsync(cancellationToken);
        return ExcelExport.Build(book, clock);
    }

    public void Dispose() => gate.Dispose();
}