using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Savings;

namespace Rose.Application.Savings;

/// <summary>
/// All the rules for changing savings goals. Kept in memory and written back to goals.json
/// on every change. A singleton shared by all requests, so every method takes the lock.
/// </summary>
public sealed class SavingsService
{
    private readonly IDataFile<List<SavingsGoal>> file;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private readonly List<SavingsGoal> goals;

    public SavingsService(IDataFile<List<SavingsGoal>> file, TimeProvider clock)
    {
        this.file = file;
        this.clock = clock;
        goals = file.Read();
    }

    /// <summary>Every goal, oldest first, with its progress worked out for today.</summary>
    public IReadOnlyList<SavingsGoalView> List()
    {
        var today = clock.Today();
        lock (gate) return [.. goals.OrderBy(g => g.CreatedAt).Select(g => SavingsRules.Summarize(g, today))];
    }

    public SavingsGoalView Create(GoalRequest request)
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
        lock (gate)
        {
            goals.Add(goal);
            return Save(goal);
        }
    }

    public SavingsGoalView Update(string id, GoalRequest request)
    {
        var input = Parse(request);
        lock (gate)
        {
            var goal = Get(id);
            goal.Name = input.Name;
            goal.Icon = input.Icon;
            goal.Target = input.Target;
            goal.Deadline = input.Deadline;
            return Save(goal);
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            goals.Remove(Get(id));
            file.Write(goals);
        }
    }

    public SavingsGoalView AddEntry(string id, EntryRequest request)
    {
        lock (gate)
        {
            var goal = Get(id);
            var input = SavingsRules.ParseEntry(
                request.Kind, request.Amount, request.Date, request.Note, SavingsRules.Saved(goal), clock.Today());
            goal.Entries.Add(new SavingsEntry(Guid.NewGuid().ToString(), input.Date, input.Amount, input.Note));
            return Save(goal);
        }
    }

    public SavingsGoalView RemoveEntry(string id, string entryId)
    {
        lock (gate)
        {
            var goal = Get(id);
            var entry = goal.Entries.Find(e => e.Id == entryId)
                ?? throw new NotFoundException("That entry no longer exists.");
            if (SavingsRules.Saved(goal) - entry.Amount < 0)
                throw new ValidationException("Deleting this deposit would leave less than zero saved - delete a withdrawal first.");
            goal.Entries.Remove(entry);
            return Save(goal);
        }
    }

    private static GoalInput Parse(GoalRequest request) =>
        SavingsRules.ParseGoal(request.Name, request.Icon, request.Target, request.Deadline);

    private SavingsGoal Get(string id) =>
        goals.Find(g => g.Id == id) ?? throw new NotFoundException("That goal no longer exists.");

    /// <summary>Stamps, writes the file and returns the fresh view. Call inside the lock.</summary>
    private SavingsGoalView Save(SavingsGoal goal)
    {
        goal.UpdatedAt = clock.GetUtcNow();
        file.Write(goals);
        return SavingsRules.Summarize(goal, clock.Today());
    }
}