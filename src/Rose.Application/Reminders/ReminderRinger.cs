using System.Globalization;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Notifications;
using Rose.Application.Users;
using Rose.Domain.Reminders;

namespace Rose.Application.Reminders;

/// <summary>
/// One scheduler step: for every user, finds what is due and rings it on that user's devices.
/// The first ring of each occurrence is also emailed; a ring after a snooze is not.
/// </summary>
public sealed partial class ReminderRinger(
    ReminderService reminders, PushService push, UserService users, EmailNotifier email, TimeProvider clock, ILogger<ReminderRinger> logger)
    : IScheduledJob
{
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
                    var due = await reminders.TickAsync(user.Id, cancellationToken);
                    await Task.WhenAll(due.Select(ring => RingAsync(user, ring, cancellationToken)));
                }
#pragma warning disable CA1031 // one user's failure must not stop the others' reminders
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

    private async Task RingAsync(RoseUser user, ReminderRing ring, CancellationToken ct)
    {
        var reminder = ring.Reminder;
        var time = clock.GetLocalNow().ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        var when = reminder.Repeat == RepeatRule.None
            ? time
            : $"{time} \u00B7 {ReminderRules.DescribeRepeat(reminder, reminders.Zone)}";
        var title = $"\u23F0 {reminder.Text}";

        var pushing = push.SendAsync(user.Id, new PushMessage(title, when, PushService.RemindersUrl, reminder.Id), ct);
        var mailing = ring.IsSnoozeRing ? Task.CompletedTask : email.SendAsync(user, title, when, PushService.RemindersUrl, ct);
        await Task.WhenAll(pushing, mailing);
        var result = await pushing;
        LogRang(reminder.Text, result.Sent, result.Failed, !ring.IsSnoozeRing && email.IsEnabled);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rang \"{Text}\" - devices: {Sent} sent, {Failed} failed; emailed: {Emailed}")]
    private partial void LogRang(string text, int sent, int failed, bool emailed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ringing one user's reminders failed")]
    private partial void LogUserFailed(Exception error);
}