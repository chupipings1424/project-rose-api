using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Savings;

namespace Rose.Application.Savings;

/// <summary>
/// All the rules for changing savings goals. A singleton shared by all requests: every method
/// holds one user's goals exclusively while it reads or changes them.
/// </summary>
public sealed class SavingsService(IRepository<SavingsGoal> repository, TimeProvider clock) : IDisposable, IUserScopedCache
{
    private readonly RepositoryCache<SavingsGoal> cache = new(repository);

    /// <summary>Every goal, oldest first, with its progress worked out for today.</summary>
    public async Task<IReadOnlyList<SavingsGoalView>> ListAsync(Guid owner, CancellationToken ct = default)
    {
        var today = clock.Today();
        using var session = await cache.OpenAsync(owner, ct);
        return [.. session.Items.OrderBy(g => g.CreatedAt).Select(g => SavingsRules.Summarize(g, today))];
    }

    public async Task<SavingsGoalView> CreateAsync(Guid owner, GoalRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        var now = clock.GetUtcNow();
        var goal = new SavingsGoal
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name,
            Icon = input.Icon,
            Target = input.Target,
            Deadline = input.Deadline,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var session = await cache.OpenAsync(owner, ct);
        session.Items.Add(goal);
        return await SaveAsync(session, goal, ct);
    }

    public Task<SavingsGoalView> UpdateAsync(Guid owner, string id, GoalRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        return ChangeAsync(owner, id, goal =>
        {
            goal.Name = input.Name;
            goal.Icon = input.Icon;
            goal.Target = input.Target;
            goal.Deadline = input.Deadline;
        }, ct);
    }

    public async Task RemoveAsync(Guid owner, string id, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var goal = Get(session, id);
        await session.DeleteAsync(goal.Id, ct);
        session.Items.Remove(goal);
    }

    public Task<SavingsGoalView> AddEntryAsync(Guid owner, string id, EntryRequest request, CancellationToken ct = default) =>
        ChangeAsync(owner, id, goal =>
        {
            var input = SavingsRules.ParseEntry(
                request.Kind, request.Amount, request.Date, request.Note, SavingsRules.Saved(goal), clock.Today());
            goal.Entries.Add(new SavingsEntry(Guid.NewGuid().ToString(), input.Date, input.Amount, input.Note));
        }, ct);

    public Task<SavingsGoalView> RemoveEntryAsync(Guid owner, string id, string entryId, CancellationToken ct = default) =>
        ChangeAsync(owner, id, goal =>
        {
            var entry = goal.Entries.Find(e => e.Id == entryId)
                ?? throw new NotFoundException("That entry no longer exists.");
            if (SavingsRules.Saved(goal) - entry.Amount < 0)
                throw new ValidationException("Deleting this deposit would leave less than zero saved - delete a withdrawal first.");
            goal.Entries.Remove(entry);
        }, ct);

    /// <summary>Forgets a deleted user's cached goals.</summary>
    public void Forget(Guid owner) => cache.Forget(owner);

    public void Dispose() => cache.Dispose();

    private static GoalInput Parse(GoalRequest request) =>
        SavingsRules.ParseGoal(request.Name, request.Icon, request.Target, request.Deadline);

    private static SavingsGoal Get(RepositoryCache<SavingsGoal>.Session session, string id) =>
        session.Items.Find(g => g.Id == id) ?? throw new NotFoundException("That goal no longer exists.");

    private async Task<SavingsGoalView> ChangeAsync(Guid owner, string id, Action<SavingsGoal> change, CancellationToken ct)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var goal = Get(session, id);
        change(goal);
        return await SaveAsync(session, goal, ct);
    }

    /// <summary>Stamps, writes and returns the fresh view.</summary>
    private async Task<SavingsGoalView> SaveAsync(RepositoryCache<SavingsGoal>.Session session, SavingsGoal goal, CancellationToken ct)
    {
        goal.UpdatedAt = clock.GetUtcNow();
        await session.SaveAsync(goal, ct);
        return SavingsRules.Summarize(goal, clock.Today());
    }
}