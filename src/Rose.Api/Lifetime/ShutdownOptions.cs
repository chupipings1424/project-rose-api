using System.ComponentModel.DataAnnotations;

namespace Rose.Api.Lifetime;

/// <summary>
/// The "Shutdown" section: lets ROSE stop itself when its last browser tab closes,
/// or when you press the power button. Off by default, so running the API from
/// Visual Studio or <c>dotnet run</c> keeps it alive; the ROSE launcher turns it on.
/// </summary>
public sealed class ShutdownOptions
{
    public const string Section = "Shutdown";

    public bool Enabled { get; set; }

    /// <summary>How long to wait after the last ROSE page disconnected (a reload reconnects well within it).</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(20);
}