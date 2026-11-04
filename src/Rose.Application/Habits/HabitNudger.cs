using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Application.Reminders;
using Rose.Domain.Habits;

namespace Rose.Application.Habits;

/// <summary>One scheduler step: nudges every habit whose reminder time has come and that isn't done yet.</summary>
public sealed partial class HabitNudger(
    HabitService habits, PushService push, TimeProvider clock, ILogger<HabitNudger> logger)
    : IScheduledJob
{
    public const string HabitsUrl = "/projects/habits";

    private int busy;

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        // A slow push round must not overlap the next tick.
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var due = await habits.CollectNudgesAsync(cancellationToken);
            await Task.WhenAll(due.Select(h => NudgeAsync(h, cancellationToken)));
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    private async Task NudgeAsync(Habit habit, CancellationToken ct)
    {
        var progress = HabitRules.Progress(habit, clock.Today());
        var icon = habit.Icon.Length > 0 ? habit.Icon : "\U0001F331";
        var result = await push.SendToAllAsync(new PushMessage($"{icon} {habit.Name}", progress, HabitsUrl, $"habit-{habit.Id}"), ct);
        LogNudged(habit.Name, result.Sent, result.Failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nudged habit \"{Name}\" - phones: {Sent} sent, {Failed} failed")]
    private partial void LogNudged(string name, int sent, int failed);
}