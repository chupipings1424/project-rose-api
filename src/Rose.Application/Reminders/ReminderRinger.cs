using System.Globalization;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Domain.Reminders;

namespace Rose.Application.Reminders;

/// <summary>One scheduler step: finds what is due and rings it on every subscribed device.</summary>
public sealed partial class ReminderRinger(
    ReminderService reminders, PushService push, TimeProvider clock, ILogger<ReminderRinger> logger)
    : IScheduledJob
{
    private int busy;

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        // A slow push round must not overlap the next tick.
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var due = await reminders.TickAsync(cancellationToken);
            await Task.WhenAll(due.Select(r => RingAsync(r, cancellationToken)));
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    private async Task RingAsync(Reminder reminder, CancellationToken ct)
    {
        var time = clock.GetLocalNow().ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        var when = reminder.Repeat == RepeatRule.None
            ? time
            : $"{time} \u00B7 {ReminderRules.DescribeRepeat(reminder, reminders.Zone)}";

        var result = await push.SendToAllAsync(
            new PushMessage($"\u23F0 {reminder.Text}", when, PushService.RemindersUrl, reminder.Id), ct);
        LogRang(reminder.Text, result.Sent, result.Failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rang \"{Text}\" - phones: {Sent} sent, {Failed} failed")]
    private partial void LogRang(string text, int sent, int failed);
}