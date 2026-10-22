using System.ComponentModel.DataAnnotations;

namespace Rose.Infrastructure.Lingo;

/// <summary>The "Lingo" section of appsettings.json.</summary>
public sealed class LingoOptions
{
    public const string Section = "Lingo";

    /// <summary>The offline translation model. A relative path is inside Rose:DataDirectory.</summary>
    [Required]
    public string ModelDirectory { get; set; } = Path.Combine("models", "nllb-200-distilled-600M");

    /// <summary>The model takes about 1 GB of memory while loaded, so it is let go after this long without a translation.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "1.00:00:00")]
    public TimeSpan UnloadAfterIdle { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>CPU threads one translation may use. More is a little faster; 2 keeps the PC responsive.</summary>
    [Range(1, 16)]
    public int Threads { get; set; } = 2;

    /// <summary>The longest translation (in tokens, roughly word pieces) of one sentence.</summary>
    [Range(16, 1024)]
    public int MaxTokens { get; set; } = 256;
}
