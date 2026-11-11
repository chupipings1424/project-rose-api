using Microsoft.EntityFrameworkCore;
using Rose.Application.Abstractions;
using Rose.Application.Reminders;
using Rose.Domain.Habits;
using Rose.Domain.Loans;
using Rose.Domain.Reminders;
using Rose.Domain.Savings;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// The shared upsert logic: load the stored rows for the given ids (children included), insert
/// what is new, copy values onto what exists, then save everything in one transaction.
/// Every read and delete is filtered by owner, and every saved row is stamped with it.
/// A short-lived DbContext per call keeps memory flat and avoids stale tracking.
/// </summary>
internal abstract class EfRepository<TDomain, TRow>(IDbContextFactory<RoseDbContext> contexts) : IRepository<TDomain>
    where TDomain : class
    where TRow : class, IOwnedRow
{
    public async Task<List<TDomain>> LoadAllAsync(Guid owner, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await Query(db).Where(OwnedBy(owner)).AsNoTracking().ToListAsync(cancellationToken);
        return [.. rows.Select(ToDomain)];
    }

    public async Task SaveAsync(Guid owner, IReadOnlyCollection<TDomain> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = items.Select(IdOf).ToList();

        // Looked up without the owner filter, so an id that belongs to someone else is noticed
        // instead of failing as a duplicate key.
        var stored = await Query(db).Where(Within(ids)).ToDictionaryAsync(RowId, cancellationToken);
        foreach (var item in items)
        {
            var wanted = ToRow(item);
            wanted.OwnerId = owner;
            if (stored.TryGetValue(IdOf(item), out var row))
            {
                if (row.OwnerId != owner && !CanChangeOwner)
                    throw new InvalidOperationException($"{typeof(TRow).Name} {IdOf(item)} belongs to another user.");
                Update(db, row, wanted);
            }
            else
            {
                db.Add(wanted);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid owner, string id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Set<TRow>().Where(Within([id])).Where(OwnedBy(owner)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// True when a row saved by another user simply moves to the saver (a phone that signs in
    /// with a different account). Otherwise saving someone else's id is a bug and throws.
    /// </summary>
    protected virtual bool CanChangeOwner => false;

    private static System.Linq.Expressions.Expression<Func<TRow, bool>> OwnedBy(Guid owner) =>
        row => EF.Property<Guid>(row, nameof(IOwnedRow.OwnerId)) == owner;
    protected abstract IQueryable<TRow> Query(RoseDbContext db);
    protected abstract string IdOf(TDomain item);
    protected abstract string RowId(TRow row);
    protected abstract System.Linq.Expressions.Expression<Func<TRow, bool>> Within(IReadOnlyCollection<string> ids);
    protected abstract TDomain ToDomain(TRow row);
    protected abstract TRow ToRow(TDomain item);

    /// <summary>Copies <paramref name="wanted"/> onto the tracked <paramref name="row"/> (children too).</summary>
    protected virtual void Update(RoseDbContext db, TRow row, TRow wanted) => db.Entry(row).CurrentValues.SetValues(wanted);

    /// <summary>Makes a tracked child collection match <paramref name="wanted"/>: update, add, remove by key.</summary>
    protected static void Sync<TChild, TKey>(
        RoseDbContext db, List<TChild> existing, IEnumerable<TChild> wanted, Func<TChild, TKey> key)
        where TChild : class
        where TKey : notnull
    {
        var byKey = existing.ToDictionary(key);
        var keep = new HashSet<TKey>();
        foreach (var child in wanted)
        {
            var k = key(child);
            keep.Add(k);
            if (byKey.TryGetValue(k, out var current)) db.Entry(current).CurrentValues.SetValues(child);
            else existing.Add(child);
        }
        foreach (var gone in byKey.Where(p => !keep.Contains(p.Key)).Select(p => p.Value))
        {
            existing.Remove(gone);
            db.Remove(gone);
        }
    }
}

internal sealed class ReminderRepository(IDbContextFactory<RoseDbContext> contexts) : EfRepository<Reminder, ReminderRow>(contexts)
{
    protected override IQueryable<ReminderRow> Query(RoseDbContext db) => db.Reminders;
    protected override string IdOf(Reminder item) => item.Id;
    protected override string RowId(ReminderRow row) => row.Id;
    protected override System.Linq.Expressions.Expression<Func<ReminderRow, bool>> Within(IReadOnlyCollection<string> ids) =>
        r => ids.Contains(r.Id);
    protected override Reminder ToDomain(ReminderRow row) => Mappers.ToDomain(row);
    protected override ReminderRow ToRow(Reminder item) => Mappers.ToRow(item);
}

internal sealed class PushDeviceRepository(IDbContextFactory<RoseDbContext> contexts) : EfRepository<PushDevice, PushDeviceRow>(contexts)
{
    protected override IQueryable<PushDeviceRow> Query(RoseDbContext db) => db.PushDevices.OrderBy(d => d.CreatedAt);
    protected override string IdOf(PushDevice item) => item.Subscription.Endpoint!;
    protected override string RowId(PushDeviceRow row) => row.Endpoint;
    protected override System.Linq.Expressions.Expression<Func<PushDeviceRow, bool>> Within(IReadOnlyCollection<string> ids) =>
        d => ids.Contains(d.Endpoint);
    protected override PushDevice ToDomain(PushDeviceRow row) => Mappers.ToDomain(row);
    protected override PushDeviceRow ToRow(PushDevice item) => Mappers.ToRow(item);

    // A browser has one endpoint; whoever subscribed it last gets its notifications.
    protected override bool CanChangeOwner => true;
}

internal sealed class SavingsGoalRepository(IDbContextFactory<RoseDbContext> contexts) : EfRepository<SavingsGoal, SavingsGoalRow>(contexts)
{
    protected override IQueryable<SavingsGoalRow> Query(RoseDbContext db) => db.SavingsGoals.Include(g => g.Entries);
    protected override string IdOf(SavingsGoal item) => item.Id;
    protected override string RowId(SavingsGoalRow row) => row.Id;
    protected override System.Linq.Expressions.Expression<Func<SavingsGoalRow, bool>> Within(IReadOnlyCollection<string> ids) =>
        g => ids.Contains(g.Id);
    protected override SavingsGoal ToDomain(SavingsGoalRow row) => Mappers.ToDomain(row);
    protected override SavingsGoalRow ToRow(SavingsGoal item) => Mappers.ToRow(item);

    protected override void Update(RoseDbContext db, SavingsGoalRow row, SavingsGoalRow wanted)
    {
        base.Update(db, row, wanted);
        Sync(db, row.Entries, wanted.Entries, e => e.Id);
    }
}

internal sealed class LoanRepository(IDbContextFactory<RoseDbContext> contexts) : EfRepository<Loan, LoanRow>(contexts)
{
    protected override IQueryable<LoanRow> Query(RoseDbContext db) => db.Loans;
    protected override string IdOf(Loan item) => item.Id;
    protected override string RowId(LoanRow row) => row.Id;
    protected override System.Linq.Expressions.Expression<Func<LoanRow, bool>> Within(IReadOnlyCollection<string> ids) =>
        l => ids.Contains(l.Id);
    protected override Loan ToDomain(LoanRow row) => Mappers.ToDomain(row);
    protected override LoanRow ToRow(Loan item) => Mappers.ToRow(item);
}

internal sealed class HabitRepository(IDbContextFactory<RoseDbContext> contexts) : EfRepository<Habit, HabitRow>(contexts)
{
    protected override IQueryable<HabitRow> Query(RoseDbContext db) => db.Habits.Include(h => h.Days);
    protected override string IdOf(Habit item) => item.Id;
    protected override string RowId(HabitRow row) => row.Id;
    protected override System.Linq.Expressions.Expression<Func<HabitRow, bool>> Within(IReadOnlyCollection<string> ids) =>
        h => ids.Contains(h.Id);
    protected override Habit ToDomain(HabitRow row) => Mappers.ToDomain(row);
    protected override HabitRow ToRow(Habit item) => Mappers.ToRow(item);

    protected override void Update(RoseDbContext db, HabitRow row, HabitRow wanted)
    {
        base.Update(db, row, wanted);
        Sync(db, row.Days, wanted.Days, d => d.Date);
    }
}