using System.ComponentModel.DataAnnotations;

namespace Rose.Infrastructure.Lingo;

/// <summary>The "Lingo" section of appsettings.json.</summary>
public sealed class LingoOptions
{
    public const string Section = "Lingo";

    /// <summary>The free MyMemory translation API.</summary>
    [Required]
    [Url]
    public string TranslatorBaseUrl { get; set; } = "https://api.mymemory.translated.net/";

    /// <summary>Optional: an e-mail raises MyMemory's free limit from 5,000 to 50,000 characters a day.</summary>
    [EmailAddress]
    public string? TranslatorEmail { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan TranslatorTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
