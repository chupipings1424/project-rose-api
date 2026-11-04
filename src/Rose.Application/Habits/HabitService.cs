using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Habits;

namespace Rose.Application.Habits;

/// <summary>
/// All the rules for changing habits. A singleton: the scheduler and HTTP requests share one
/// cache, and every method holds one user's habits exclusively while it reads or changes them.
/// </summary>
public sealed class HabitService(IRepository<Habit> repository, TimeProvider clock) : IDisposable, IUserScopedCache
{
    private readonly RepositoryCache<Habit> cache = new(repository);

    /// <summary>Every habit, oldest first, with today's progress and streaks.</summary>
    public async Task<IReadOnlyList<HabitView>> ListAsync(Guid owner, CancellationToken ct = default)
    {
        var today = clock.Today();
        using var session = await cache.OpenAsync(owner, ct);
        return [.. session.Items.OrderBy(h => h.CreatedAt).Select(h => HabitRules.Summarize(h, today))];
    }

    public async Task<HabitView> CreateAsync(Guid owner, HabitRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        var now = clock.GetUtcNow();
        var habit = new Habit
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name,
            Kind = input.Kind,
            Target = input.Target,
            CreatedAt = now,
            UpdatedAt = now,
        };
        HabitRules.Apply(habit, input);
        HabitRules.SetRemindAt(habit, input.RemindAt, clock.GetLocalNow());
        using var session = await cache.OpenAsync(owner, ct);
        session.Items.Add(habit);
        return await SaveAsync(session, habit, ct);
    }

    public Task<HabitView> UpdateAsync(Guid owner, string id, HabitRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        return ChangeAsync(owner, id, habit =>
        {
            HabitRules.Apply(habit, input);
            HabitRules.SetRemindAt(habit, input.RemindAt, clock.GetLocalNow());
        }, ct);
    }

    public async Task RemoveAsync(Guid owner, string id, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var habit = Get(session, id);
        await session.DeleteAsync(habit.Id, ct);
        session.Items.Remove(habit);
    }

    /// <summary>Sets how much was done on one day. Idempotent: sending the same value twice is harmless.</summary>
    public Task<HabitView> SetDayAsync(Guid owner, string id, string? date, int? value, CancellationToken ct = default)
    {
        var day = HabitRules.ParseDay(date, clock.Today());
        return ChangeAsync(owner, id, habit => HabitRules.SetDay(habit, day, HabitRules.ParseValue(habit, value)), ct);
    }

    /// <summary>"Mark as done" from a nudge: today is complete.</summary>
    public Task<HabitView> CompleteTodayAsync(Guid owner, string id, CancellationToken ct = default) =>
        ChangeAsync(owner, id, habit =>
        {
            HabitRules.SetDay(habit, clock.Today(), habit.Target);
            habit.SnoozedUntil = null;
        }, ct);

    /// <summary>"Dismiss" a nudge: no more nudges today.</summary>
    public Task<HabitView> DismissNudgeAsync(Guid owner, string id, CancellationToken ct = default) =>
        ChangeAsync(owner, id, habit =>
        {
            habit.SnoozedUntil = null;
            habit.NudgedOn = clock.Today();
        }, ct);

    public Task<HabitView> SnoozeNudgeAsync(Guid owner, string id, double? minutes, CancellationToken ct = default)
    {
        var value = minutes ?? 10;
        if (!double.IsFinite(value) || value < 1 || value > 24 * 60)
            throw new ValidationException("Snooze between 1 minute and 24 hours.");
        return ChangeAsync(owner, id, habit =>
        {
            habit.SnoozedUntil = clock.GetUtcNow().AddMinutes(value);
            habit.NudgedOn = clock.Today();
        }, ct);
    }

    /// <summary>Called by the scheduler for each user. Returns the habits to nudge now (and saves that they were nudged).</summary>
    public async Task<IReadOnlyList<HabitNudge>> CollectNudgesAsync(Guid owner, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var due = HabitRules.CollectNudges(session.Items, clock.GetLocalNow());
        if (due.Count > 0) await session.SaveAsync([.. due.Select(n => n.Habit)], ct);
        return [.. due.Select(n => n with { Habit = n.Habit.Copy() })];
    }

    /// <summary>Forgets a deleted user's cached habits.</summary>
    public void Forget(Guid owner) => cache.Forget(owner);

    public void Dispose() => cache.Dispose();

    private static HabitInput Parse(HabitRequest request) =>
        HabitRules.ParseHabit(request.Name, request.Icon, request.Kind, request.Target, request.Unit, request.RemindAt);

    private static Habit Get(RepositoryCache<Habit>.Session session, string id) =>
        session.Items.Find(h => h.Id == id) ?? throw new NotFoundException("That habit no longer exists.");

    private async Task<HabitView> ChangeAsync(Guid owner, string id, Action<Habit> change, CancellationToken ct)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var habit = Get(session, id);
        change(habit);
        return await SaveAsync(session, habit, ct);
    }

    /// <summary>Stamps, writes and returns the fresh view.</summary>
    private async Task<HabitView> SaveAsync(RepositoryCache<Habit>.Session session, Habit habit, CancellationToken ct)
    {
        habit.UpdatedAt = clock.GetUtcNow();
        await session.SaveAsync(habit, ct);
        return HabitRules.Summarize(habit, clock.Today());
    }
}