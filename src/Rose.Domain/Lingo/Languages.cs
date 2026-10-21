using Rose.Domain.Common;

namespace Rose.Domain.Lingo;

/// <param name="Code">ISO 639-1 code, e.g. "ja".</param>
/// <param name="Speech">BCP 47 tag the browser's text-to-speech uses, e.g. "ja-JP".</param>
public sealed record LanguageInfo(string Code, string Name, string NativeName, string Speech, bool HasReading);

/// <summary>The languages ROSE Lingo can translate and teach.</summary>
public static class Languages
{
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string German = "de";
    public const string English = "en";

    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new(Japanese, "Japanese", "日本語", "ja-JP", HasReading: true),
        new(Korean, "Korean", "한국어", "ko-KR", HasReading: true),
        new(German, "German", "Deutsch", "de-DE", HasReading: false),
        new(English, "English", "English", "en-US", HasReading: false),
    ];

    public static bool IsKnown(string? code) => All.Any(l => l.Code == code);

    /// <summary>Normalises " JA " to "ja", or throws a 400 naming the field.</summary>
    public static string Require(string? code, string field = "language")
    {
        var normalised = (code ?? "").Trim().ToLowerInvariant();
        return IsKnown(normalised)
            ? normalised
            : throw new ValidationException($"{field} must be one of {string.Join(", ", All.Select(l => l.Code))}.");
    }
}
