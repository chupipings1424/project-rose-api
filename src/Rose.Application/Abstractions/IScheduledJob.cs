namespace Rose.Application.Abstractions;

/// <summary>
/// Something the background scheduler runs every few seconds (reminders ringing, habit nudges).
/// Each job decides for itself whether anything is due; one failing job never stops the others.
/// </summary>
public interface IScheduledJob
{
    Task TickAsync(CancellationToken cancellationToken = default);
}