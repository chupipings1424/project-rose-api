using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Rose.Application.Abstractions;
using Rose.Infrastructure.Excel;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// The payment tracker and budget in PostgreSQL. One session at a time per user (a semaphore
/// each), so two requests can never interleave their read-change-save cycles, while different
/// users never wait for each other.
/// </summary>
public sealed class PostgresTrackerStore(IDbContextFactory<RoseDbContext> contexts, TimeProvider clock)
    : ITrackerWorkbookStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();

    public string StorageName => "Supabase Postgres";

    public async Task<ITrackerWorkbook> OpenAsync(Guid owner, CancellationToken cancellationToken = default)
    {
        var gate = gates.GetOrAdd(owner, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        RoseDbContext? db = null;
        try
        {
            db = await contexts.CreateDbContextAsync(cancellationToken);
            var accounts = await db.Accounts.Where(a => a.OwnerId == owner).OrderBy(a => a.Seq).ToListAsync(cancellationToken);
            var payments = await db.Payments.Where(p => p.OwnerId == owner).OrderBy(p => p.Seq).ToListAsync(cancellationToken);
            var lines = await db.BudgetLines.Where(l => l.OwnerId == owner).OrderBy(l => l.Seq).ToListAsync(cancellationToken);
            return new PostgresTrackerBook(db, owner, accounts, payments, lines, () => gate.Release());
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            gate.Release();
            throw;
        }
    }

    /// <summary>One user's data as a payments.xlsx (Accounts, Payments, Budget and the yearly report sheets).</summary>
    public async Task<byte[]> ExportAsync(Guid owner, CancellationToken cancellationToken = default)
    {
        using var book = await OpenAsync(owner, cancellationToken);
        return ExcelExport.Build(book, clock);
    }

    public void Dispose()
    {
        foreach (var gate in gates.Values) gate.Dispose();
    }
}