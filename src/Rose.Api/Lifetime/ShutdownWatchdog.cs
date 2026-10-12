using Microsoft.Extensions.Options;

namespace Rose.Api.Lifetime;

/// <summary>Stops the API once no ROSE page has been open for <see cref="ShutdownOptions.IdleTimeout"/>.</summary>
internal sealed partial class ShutdownWatchdog(
    PresenceTracker presence,
    IOptions<ShutdownOptions> options,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<ShutdownWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (presence.IdleFor() >= options.Value.IdleTimeout)
            {
                LogIdle(options.Value.IdleTimeout.TotalSeconds);
                lifetime.StopApplication();
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No ROSE page open for {Seconds}s - shutting down")]
    private partial void LogIdle(double seconds);
}