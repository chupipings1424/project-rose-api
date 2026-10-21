using Rose.Domain.Common;

namespace Rose.Domain.Lingo;

/// <param name="Code">ISO 639-1 code, e.g. "ja", or a script tag for Chinese, e.g. "zh-Hans".</param>
/// <param name="Speech">BCP 47 tag the browser's text-to-speech uses, e.g. "ja-JP".</param>
/// <param name="HasReading">True when cards get Latin letters (romaji, romanization or pinyin).</param>
public sealed record LanguageInfo(string Code, string Name, string NativeName, string Speech, bool HasReading);

/// <summary>The languages ROSE Lingo can translate and teach.</summary>
public static class Languages
{
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string German = "de";
    public const string English = "en";
    public const string Tagalog = "tl";
    public const string ChineseSimplified = "zh-Hans";
    public const string ChineseTraditional = "zh-Hant";

    /// <summary>The longest code, so the database column can be sized for it.</summary>
    public const int MaxCodeLength = 7;

    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new(Tagalog, "Tagalog", "Tagalog", "fil-PH", HasReading: false),
        new(English, "English", "English", "en-US", HasReading: false),
        new(Japanese, "Japanese", "日本語", "ja-JP", HasReading: true),
        new(Korean, "Korean", "한국어", "ko-KR", HasReading: true),
        new(ChineseSimplified, "Chinese (Simplified)", "简体中文", "zh-CN", HasReading: true),
        new(ChineseTraditional, "Chinese (Traditional)", "繁體中文", "zh-TW", HasReading: true),
        new(German, "German", "Deutsch", "de-DE", HasReading: false),
    ];

    public static bool IsKnown(string? code) => All.Any(l => l.Code == code);

    public static bool IsChinese(string code) => code is ChineseSimplified or ChineseTraditional;

    /// <summary>Normalises " JA " to "ja" and "zh-hans" to "zh-Hans", or throws a 400 naming the field.</summary>
    public static string Require(string? code, string field = "language")
    {
        var trimmed = (code ?? "").Trim();
        return All.FirstOrDefault(l => string.Equals(l.Code, trimmed, StringComparison.OrdinalIgnoreCase))?.Code
            ?? throw new ValidationException($"{field} must be one of {string.Join(", ", All.Select(l => l.Code))}.");
    }
}
