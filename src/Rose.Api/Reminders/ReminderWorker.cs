using Microsoft.Extensions.Options;
using Rose.Application.Reminders;
using Rose.Infrastructure;

namespace Rose.Api.Reminders;

/// <summary>The scheduler: every few seconds, ring whatever is due.</summary>
internal sealed partial class ReminderWorker(
    ReminderRinger ringer, IOptions<ReminderOptions> options, TimeProvider clock, ILogger<ReminderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SchedulerEnabled) return;
        using var timer = new PeriodicTimer(options.Value.TickInterval, clock);
        do
        {
            try
            {
                await ringer.TickAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogTickFailed(error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminder scheduler tick failed")]
    private partial void LogTickFailed(Exception error);
}