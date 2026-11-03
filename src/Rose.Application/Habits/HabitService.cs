using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Habits;

namespace Rose.Application.Habits;

/// <summary>
/// All the rules for changing habits. Kept in memory and written back to habits.json on every
/// change. A singleton: the scheduler and HTTP requests share it, so every method takes the lock.
/// </summary>
public sealed class HabitService
{
    private readonly IDataFile<List<Habit>> file;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private readonly List<Habit> habits;

    public HabitService(IDataFile<List<Habit>> file, TimeProvider clock)
    {
        this.file = file;
        this.clock = clock;
        habits = file.Read();
    }

    /// <summary>Every habit, oldest first, with today's progress and streaks.</summary>
    public IReadOnlyList<HabitView> List()
    {
        var today = clock.Today();
        lock (gate) return [.. habits.OrderBy(h => h.CreatedAt).Select(h => HabitRules.Summarize(h, today))];
    }

    public HabitView Create(HabitRequest request)
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
        lock (gate)
        {
            habits.Add(habit);
            return Save(habit);
        }
    }

    public HabitView Update(string id, HabitRequest request)
    {
        var input = Parse(request);
        lock (gate)
        {
            var habit = Get(id);
            HabitRules.Apply(habit, input);
            HabitRules.SetRemindAt(habit, input.RemindAt, clock.GetLocalNow());
            return Save(habit);
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            habits.Remove(Get(id));
            file.Write(habits);
        }
    }

    /// <summary>Sets how much was done on one day. Idempotent: sending the same value twice is harmless.</summary>
    public HabitView SetDay(string id, string? date, int? value)
    {
        var day = HabitRules.ParseDay(date, clock.Today());
        lock (gate)
        {
            var habit = Get(id);
            HabitRules.SetDay(habit, day, HabitRules.ParseValue(habit, value));
            return Save(habit);
        }
    }

    /// <summary>"Mark as done" on the nudge popup: today is complete.</summary>
    public HabitView CompleteToday(string id)
    {
        lock (gate)
        {
            var habit = Get(id);
            HabitRules.SetDay(habit, clock.Today(), habit.Target);
            habit.SnoozedUntil = null;
            return Save(habit);
        }
    }

    /// <summary>"Dismiss" on the nudge popup: no more nudges today.</summary>
    public HabitView DismissNudge(string id)
    {
        lock (gate)
        {
            var habit = Get(id);
            habit.SnoozedUntil = null;
            habit.NudgedOn = clock.Today();
            return Save(habit);
        }
    }

    public HabitView SnoozeNudge(string id, double? minutes)
    {
        var value = minutes ?? 10;
        if (!double.IsFinite(value) || value < 1 || value > 24 * 60)
            throw new ValidationException("Snooze between 1 minute and 24 hours.");
        lock (gate)
        {
            var habit = Get(id);
            habit.SnoozedUntil = clock.GetUtcNow().AddMinutes(value);
            habit.NudgedOn = clock.Today();
            return Save(habit);
        }
    }

    /// <summary>Called by the scheduler. Returns the habits to nudge now (and saves that they were nudged).</summary>
    public IReadOnlyList<Habit> CollectNudges()
    {
        lock (gate)
        {
            var due = HabitRules.CollectNudges(habits, clock.GetLocalNow());
            if (due.Count > 0) file.Write(habits);
            return [.. due.Select(h => h.Copy())];
        }
    }

    private static HabitInput Parse(HabitRequest request) =>
        HabitRules.ParseHabit(request.Name, request.Icon, request.Kind, request.Target, request.Unit, request.RemindAt);

    private Habit Get(string id) =>
        habits.Find(h => h.Id == id) ?? throw new NotFoundException("That habit no longer exists.");

    /// <summary>Stamps, writes the file and returns the fresh view. Call inside the lock.</summary>
    private HabitView Save(Habit habit)
    {
        habit.UpdatedAt = clock.GetUtcNow();
        file.Write(habits);
        return HabitRules.Summarize(habit, clock.Today());
    }
}