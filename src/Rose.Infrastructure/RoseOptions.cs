using System.ComponentModel.DataAnnotations;

namespace Rose.Infrastructure;

/// <summary>The "Rose" section of appsettings.json.</summary>
public sealed class RoseOptions
{
    public const string Section = "Rose";

    /// <summary>Folder for payments.xlsx and the reminders JSON files. Relative paths start at the API project.</summary>
    [Required]
    public string DataDirectory { get; set; } = "data";

    /// <summary>Optional full path to the workbook; defaults to DataDirectory/payments.xlsx.</summary>
    public string? WorkbookPath { get; set; }

    public string ResolvedWorkbookPath => WorkbookPath is { Length: > 0 } path ? path : Path.Combine(DataDirectory, "payments.xlsx");
}

/// <summary>The "Reminders" section of appsettings.json.</summary>
public sealed class ReminderOptions
{
    public const string Section = "Reminders";

    /// <summary>How often the scheduler checks what is due.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Turn the background scheduler off (used by the integration tests).</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>The always-on-top popup on this Windows PC.</summary>
    public bool DesktopAlarms { get; set; } = true;

    /// <summary>Who to contact about this push sender (a URL or mailto:), sent to Apple / Google.</summary>
    public string PushContact { get; set; } = "https://github.com/chupipings1424/project-rose";
}