using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rose.Application.Abstractions;
using Rose.Application.Users;
using Rose.Domain.Common;

namespace Rose.Infrastructure.Persistence;

/// <summary>The rose.users table, and moving or deleting every row a user owns.</summary>
internal sealed class PostgresUserStore(IDbContextFactory<RoseDbContext> contexts) : IUserStore
{
    public async Task<List<RoseUser>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(cancellationToken);
        return [.. rows.Select(u => new RoseUser(u.Id, u.Email, u.MustChangePassword, u.CreatedAt))];
    }

    public async Task AddAsync(RoseUser user, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Users.Add(new UserRow
        {
            Id = user.Id,
            Email = user.Email,
            MustChangePassword = user.MustChangePassword,
            CreatedAt = user.CreatedAt.ToUniversalTime(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("That email already has a ROSE account.");
        }
    }

    public async Task SetMustChangePasswordAsync(Guid id, bool value, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, value), cancellationToken);
    }

    public Task<int> ClaimUnownedDataAsync(Guid owner, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async db =>
        {
            var moved = 0;
            moved += await Claim(db.Accounts);
            moved += await Claim(db.Payments);
            moved += await Claim(db.BudgetLines);
            moved += await Claim(db.Reminders);
            moved += await Claim(db.PushDevices);
            moved += await Claim(db.SavingsGoals);
            moved += await Claim(db.Habits);
            return moved;

            Task<int> Claim<TRow>(DbSet<TRow> rows)
                where TRow : class, IOwnedRow =>
                rows.Where(r => EF.Property<Guid>(r, nameof(IOwnedRow.OwnerId)) == Guid.Empty)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<Guid>(r, nameof(IOwnedRow.OwnerId)), owner), cancellationToken);
        }, cancellationToken);

    public Task DeleteWithDataAsync(Guid id, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async db =>
        {
            // Savings entries and habit days go with their parent (ON DELETE CASCADE).
            await Delete(db.Accounts);
            await Delete(db.Payments);
            await Delete(db.BudgetLines);
            await Delete(db.Reminders);
            await Delete(db.PushDevices);
            await Delete(db.SavingsGoals);
            await Delete(db.Habits);
            await Delete(db.IdempotencyKeys);
            await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
            return 0;

            Task<int> Delete<TRow>(DbSet<TRow> rows)
                where TRow : class, IOwnedRow =>
                rows.Where(r => EF.Property<Guid>(r, nameof(IOwnedRow.OwnerId)) == id).ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

    /// <summary>All or nothing. Runs through the retry strategy, which owns the whole transaction.</summary>
    private async Task<T> InTransactionAsync<T>(Func<RoseDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work(db);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}