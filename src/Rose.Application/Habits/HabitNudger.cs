using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Application.Notifications;
using Rose.Application.Reminders;
using Rose.Application.Users;
using Rose.Domain.Habits;

namespace Rose.Application.Habits;

/// <summary>
/// One scheduler step: for every user, nudges each habit whose reminder time has come and that
/// isn't done yet. Today's first nudge is also emailed; a nudge after a snooze is not.
/// </summary>
public sealed partial class HabitNudger(
    HabitService habits, PushService push, UserService users, EmailNotifier email, TimeProvider clock, ILogger<HabitNudger> logger)
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
            foreach (var user in await users.AllAsync(cancellationToken))
            {
                try
                {
                    var due = await habits.CollectNudgesAsync(user.Id, cancellationToken);
                    await Task.WhenAll(due.Select(nudge => NudgeAsync(user, nudge, cancellationToken)));
                }
#pragma warning disable CA1031 // one user's failure must not stop the others' nudges
                catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    LogUserFailed(error);
                }
            }
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    private async Task NudgeAsync(RoseUser user, HabitNudge nudge, CancellationToken ct)
    {
        var habit = nudge.Habit;
        var progress = HabitRules.Progress(habit, clock.Today());
        var icon = habit.Icon.Length > 0 ? habit.Icon : "\U0001F331";
        var title = $"{icon} {habit.Name}";

        var pushing = push.SendAsync(user.Id, new PushMessage(title, progress, HabitsUrl, $"habit-{habit.Id}"), ct);
        var mailing = nudge.IsSnoozeNudge ? Task.CompletedTask : email.SendAsync(user, title, progress, HabitsUrl, ct);
        await Task.WhenAll(pushing, mailing);
        var result = await pushing;
        LogNudged(habit.Name, result.Sent, result.Failed, !nudge.IsSnoozeNudge && email.IsEnabled);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nudged habit \"{Name}\" - devices: {Sent} sent, {Failed} failed; emailed: {Emailed}")]
    private partial void LogNudged(string name, int sent, int failed, bool emailed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Nudging one user's habits failed")]
    private partial void LogUserFailed(Exception error);
}