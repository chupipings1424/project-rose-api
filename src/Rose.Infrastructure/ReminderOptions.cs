using System.ComponentModel.DataAnnotations;

namespace Rose.Infrastructure;

/// <summary>The "Reminders" section of appsettings.json.</summary>
public sealed class ReminderOptions
{
    public const string Section = "Reminders";

    /// <summary>How often the scheduler checks what is due.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Turn the background scheduler off (used by the integration tests).</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Who to contact about this push sender (a URL or mailto:), sent to Apple / Google.</summary>
    public string PushContact { get; set; } = "https://github.com/chupipings1424/project-rose";
}

/// <summary>
/// The "Push" section. Optional: when both keys are set they are used, otherwise a key pair is
/// created once and kept in the database (table app_secrets).
/// </summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }
}