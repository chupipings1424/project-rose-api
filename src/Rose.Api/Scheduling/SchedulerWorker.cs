using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Infrastructure;

namespace Rose.Api.Scheduling;

/// <summary>
/// The scheduler: every few seconds, give each <see cref="IScheduledJob"/> (reminders, habit
/// nudges) a chance to run. A failing job is logged and never stops the others.
/// </summary>
internal sealed partial class SchedulerWorker(
    IEnumerable<IScheduledJob> jobs, IOptions<ReminderOptions> options, TimeProvider clock, ILogger<SchedulerWorker> logger)
    : BackgroundService
{
    private readonly IReadOnlyList<IScheduledJob> jobs = [.. jobs];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SchedulerEnabled) return;
        using var timer = new PeriodicTimer(options.Value.TickInterval, clock);
        do
        {
            await Task.WhenAll(jobs.Select(job => RunAsync(job, stoppingToken)));
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(IScheduledJob job, CancellationToken ct)
    {
        try
        {
            await job.TickAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            LogTickFailed(job.GetType().Name, error);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled job {Job} failed")]
    private partial void LogTickFailed(string job, Exception error);
}