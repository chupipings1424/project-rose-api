using Rose.Application.Abstractions;
using Rose.Domain.Common;
using Rose.Domain.Reminders;

namespace Rose.Application.Reminders;

/// <summary>
/// All the rules for changing reminders. Kept in memory and written back on every change.
/// A singleton: the scheduler and HTTP requests share it, so every method takes the lock.
/// </summary>
public sealed class ReminderService
{
    private readonly IDataFile<List<Reminder>> file;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private List<Reminder> reminders;

    public ReminderService(IDataFile<List<Reminder>> file, TimeProvider clock)
    {
        this.file = file;
        this.clock = clock;
        reminders = file.Read();
    }

    public TimeZoneInfo Zone => clock.LocalTimeZone;

    public IReadOnlyList<Reminder> List()
    {
        lock (gate) return [.. reminders.OrderBy(r => r.DueAt).Select(r => r.Copy())];
    }

    public Reminder Create(ReminderRequest request)
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
        lock (gate)
        {
            reminders.Add(reminder);
            Save();
            return reminder.Copy();
        }
    }

    public Reminder Update(string id, ReminderRequest request)
    {
        var input = Parse(request);
        lock (gate)
        {
            var reminder = Get(id);
            var timeChanged = reminder.DueAt != input.DueAt || reminder.Repeat != input.Repeat;
            reminder.Text = input.Text;
            reminder.DueAt = input.DueAt;
            reminder.Repeat = input.Repeat;
            reminder.WeekDays = [.. input.WeekDays];
            reminder.UpdatedAt = Now();
            if (timeChanged)
            {
                // A new time means it should ring again, even if the old time already rang.
                reminder.FiredFor = null;
                reminder.Ringing = false;
                reminder.SnoozedUntil = null;
                reminder.Done = false;
            }
            Save();
            return reminder.Copy();
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            reminders.Remove(Get(id));
            Save();
        }
    }

    /// <summary>One-off: mark done. Repeating: just silence this ring; it keeps its schedule.</summary>
    public Reminder Complete(string id) => Change(id, r =>
    {
        Silence(r);
        if (r.Repeat == RepeatRule.None) r.Done = true;
    });

    /// <summary>Silence it (and cancel any snooze) without finishing: it stays overdue / on its schedule.</summary>
    public Reminder Dismiss(string id) => Change(id, Silence);

    public Reminder Snooze(string id, double? minutes)
    {
        var value = minutes ?? 10;
        if (!double.IsFinite(value) || value < 1 || value > 24 * 60)
            throw new ValidationException("Snooze between 1 minute and 24 hours.");
        return Change(id, r =>
        {
            r.Ringing = false;
            r.SnoozedUntil = ReminderRules.TruncateToMilliseconds(Now().AddMinutes(value));
        });
    }

    /// <summary>Called by the scheduler. Returns what must ring now (and saves the new state).</summary>
    public IReadOnlyList<Reminder> Tick()
    {
        lock (gate)
        {
            var fired = ReminderRules.CollectDue(reminders, Now(), Zone);
            if (fired.Count > 0) Save();
            return [.. fired.Select(r => r.Copy())];
        }
    }

    private static ReminderInput Parse(ReminderRequest request) =>
        ReminderRules.Parse(request.Text, request.DueAt, request.Repeat, request.WeekDays);

    private static void Silence(Reminder reminder)
    {
        reminder.Ringing = false;
        reminder.SnoozedUntil = null;
    }

    private Reminder Change(string id, Action<Reminder> change)
    {
        lock (gate)
        {
            var reminder = Get(id);
            change(reminder);
            reminder.UpdatedAt = Now();
            Save();
            return reminder.Copy();
        }
    }

    private Reminder Get(string id) =>
        reminders.Find(r => r.Id == id) ?? throw new NotFoundException("That reminder no longer exists.");

    private DateTimeOffset Now() => ReminderRules.TruncateToMilliseconds(clock.GetUtcNow());

    private void Save() => file.Write(reminders);
}