namespace Rose.Domain.Lingo;

/// <param name="Letter">The jamo (letter), e.g. "ㄱ".</param>
/// <param name="Group">"consonant", "double", "vowel" or "compound".</param>
/// <param name="Name">What Koreans call the letter, e.g. "기역 (giyeok)"; vowels are named by their sound.</param>
/// <param name="Syllable">The letter in a real block: consonant + ㅏ (가), or silent ㅇ + vowel (아).</param>
public sealed record HangulEntry(string Letter, string Romanization, string Group, string Name, string Syllable, string SyllableRomanization);

/// <summary>The 40 Hangul letters, for the alphabet chart and drill.</summary>
public static class HangulChart
{
    private const int Silent = 11; // ㅇ
    private const int VowelA = 0; // ㅏ

    // (letter, romanization, index as an initial, Korean name)
    private static readonly (string Letter, string Roman, int Index, string Name)[] Consonants =
    [
        ("ㄱ", "g / k", 0, "기역 (giyeok)"), ("ㄴ", "n", 2, "니은 (nieun)"), ("ㄷ", "d / t", 3, "디귿 (digeut)"),
        ("ㄹ", "r / l", 5, "리을 (rieul)"), ("ㅁ", "m", 6, "미음 (mieum)"), ("ㅂ", "b / p", 7, "비읍 (bieup)"),
        ("ㅅ", "s", 9, "시옷 (siot)"), ("ㅇ", "silent / ng", 11, "이응 (ieung)"), ("ㅈ", "j", 12, "지읒 (jieut)"),
        ("ㅊ", "ch", 14, "치읓 (chieut)"), ("ㅋ", "k", 15, "키읔 (kieuk)"), ("ㅌ", "t", 16, "티읕 (tieut)"),
        ("ㅍ", "p", 17, "피읖 (pieup)"), ("ㅎ", "h", 18, "히읗 (hieut)"),
    ];

    private static readonly (string Letter, string Roman, int Index, string Name)[] Doubles =
    [
        ("ㄲ", "kk", 1, "쌍기역 (ssanggiyeok)"), ("ㄸ", "tt", 4, "쌍디귿 (ssangdigeut)"), ("ㅃ", "pp", 8, "쌍비읍 (ssangbieup)"),
        ("ㅆ", "ss", 10, "쌍시옷 (ssangsiot)"), ("ㅉ", "jj", 13, "쌍지읒 (ssangjieut)"),
    ];

    // (letter, romanization, index as a vowel)
    private static readonly (string Letter, string Roman, int Index)[] Vowels =
    [
        ("ㅏ", "a", 0), ("ㅑ", "ya", 2), ("ㅓ", "eo", 4), ("ㅕ", "yeo", 6), ("ㅗ", "o", 8),
        ("ㅛ", "yo", 12), ("ㅜ", "u", 13), ("ㅠ", "yu", 17), ("ㅡ", "eu", 18), ("ㅣ", "i", 20),
    ];

    private static readonly (string Letter, string Roman, int Index)[] Compounds =
    [
        ("ㅐ", "ae", 1), ("ㅒ", "yae", 3), ("ㅔ", "e", 5), ("ㅖ", "ye", 7), ("ㅘ", "wa", 9), ("ㅙ", "wae", 10),
        ("ㅚ", "oe", 11), ("ㅝ", "wo", 14), ("ㅞ", "we", 15), ("ㅟ", "wi", 16), ("ㅢ", "ui", 19),
    ];

    public static readonly IReadOnlyList<HangulEntry> Chart =
        Consonants.Select(c => Consonant(c, "consonant"))
            .Concat(Doubles.Select(c => Consonant(c, "double")))
            .Concat(Vowels.Select(v => Vowel(v, "vowel")))
            .Concat(Compounds.Select(v => Vowel(v, "compound")))
            .ToList();

    /// <summary>
    /// Every Hangul block is computed: 0xAC00 + (initial * 21 + vowel) * 28 + final.
    /// That is why Korean fits in one neat Unicode range.
    /// </summary>
    public static string Compose(int initial, int vowel, int final = 0) =>
        ((char)(0xAC00 + ((initial * 21) + vowel) * 28 + final)).ToString();

    private static HangulEntry Consonant((string Letter, string Roman, int Index, string Name) c, string group)
    {
        var syllable = Compose(c.Index, VowelA);
        return new(c.Letter, c.Roman, group, c.Name, syllable, Hangul.ToRomanization(syllable) ?? "");
    }

    private static HangulEntry Vowel((string Letter, string Roman, int Index) v, string group)
    {
        var syllable = Compose(Silent, v.Index);
        return new(v.Letter, v.Roman, group, v.Roman, syllable, Hangul.ToRomanization(syllable) ?? "");
    }
}
