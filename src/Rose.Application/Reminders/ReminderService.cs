using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Reminders;

namespace Rose.Application.Reminders;

/// <summary>
/// All the rules for changing reminders. A singleton: the scheduler and HTTP requests share one
/// cache, and every method holds it exclusively while it reads or changes a reminder.
/// </summary>
public sealed class ReminderService(IRepository<Reminder> repository, TimeProvider clock) : IDisposable
{
    private readonly RepositoryCache<Reminder> cache = new(repository);

    public TimeZoneInfo Zone => clock.LocalTimeZone;

    public async Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(ct);
        return [.. session.Items.OrderBy(r => r.DueAt).Select(r => r.Copy())];
    }

    public async Task<Reminder> CreateAsync(ReminderRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        var now = Now();
        var reminder = new Reminder
        {
            Id = Guid.NewGuid().ToString(),
            Text = input.Text,
            DueAt = input.DueAt,
            Repeat = input.Repeat,
            WeekDays = [.. input.WeekDays],
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var session = await cache.OpenAsync(ct);
        session.Items.Add(reminder);
        await session.SaveAsync(reminder, ct);
        return reminder.Copy();
    }

    public Task<Reminder> UpdateAsync(string id, ReminderRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        return ChangeAsync(id, reminder =>
        {
            var timeChanged = reminder.DueAt != input.DueAt || reminder.Repeat != input.Repeat;
            reminder.Text = input.Text;
            reminder.DueAt = input.DueAt;
            reminder.Repeat = input.Repeat;
            reminder.WeekDays = [.. input.WeekDays];
            if (timeChanged)
            {
                // A new time means it should ring again, even if the old time already rang.
                reminder.FiredFor = null;
                reminder.Ringing = false;
                reminder.SnoozedUntil = null;
                reminder.Done = false;
            }
        }, ct);
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(ct);
        var reminder = Get(session, id);
        await session.DeleteAsync(reminder.Id, ct);
        session.Items.Remove(reminder);
    }

    /// <summary>One-off: mark done. Repeating: just silence this ring; it keeps its schedule.</summary>
    public Task<Reminder> CompleteAsync(string id, CancellationToken ct = default) => ChangeAsync(id, r =>
    {
        Silence(r);
        if (r.Repeat == RepeatRule.None) r.Done = true;
    }, ct);

    /// <summary>Silence it (and cancel any snooze) without finishing: it stays overdue / on its schedule.</summary>
    public Task<Reminder> DismissAsync(string id, CancellationToken ct = default) => ChangeAsync(id, Silence, ct);

    public Task<Reminder> SnoozeAsync(string id, double? minutes, CancellationToken ct = default)
    {
        var value = minutes ?? 10;
        if (!double.IsFinite(value) || value < 1 || value > 24 * 60)
            throw new ValidationException("Snooze between 1 minute and 24 hours.");
        return ChangeAsync(id, r =>
        {
            r.Ringing = false;
            r.SnoozedUntil = ReminderRules.TruncateToMilliseconds(Now().AddMinutes(value));
        }, ct);
    }

    /// <summary>Called by the scheduler. Returns what must ring now (and saves the new state).</summary>
    public async Task<IReadOnlyList<Reminder>> TickAsync(CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(ct);
        var fired = ReminderRules.CollectDue(session.Items, Now(), Zone);
        if (fired.Count > 0) await session.SaveAsync(fired, ct);
        return [.. fired.Select(r => r.Copy())];
    }

    public void Dispose() => cache.Dispose();

    private static ReminderInput Parse(ReminderRequest request) =>
        ReminderRules.Parse(request.Text, request.DueAt, request.Repeat, request.WeekDays);

    private static void Silence(Reminder reminder)
    {
        reminder.Ringing = false;
        reminder.SnoozedUntil = null;
    }

    private static Reminder Get(RepositoryCache<Reminder>.Session session, string id) =>
        session.Items.Find(r => r.Id == id) ?? throw new NotFoundException("That reminder no longer exists.");

    private async Task<Reminder> ChangeAsync(string id, Action<Reminder> change, CancellationToken ct)
    {
        using var session = await cache.OpenAsync(ct);
        var reminder = Get(session, id);
        change(reminder);
        reminder.UpdatedAt = Now();
        await session.SaveAsync(reminder, ct);
        return reminder.Copy();
    }

    private DateTimeOffset Now() => ReminderRules.TruncateToMilliseconds(clock.GetUtcNow());
}