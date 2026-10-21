using System.Text;

namespace Rose.Domain.Lingo;

/// <param name="Group">"basic", "dakuten" (voiced marks) or "combo" (small ya/yu/yo).</param>
public sealed record KanaEntry(string Hiragana, string Katakana, string Romaji, string Group);

/// <summary>Hiragana and katakana to romaji (Hepburn), plus the chart used by the kana drills.</summary>
public static class Kana
{
    private static readonly (string Kana, string Romaji)[] Basic =
    [
        ("あ", "a"), ("い", "i"), ("う", "u"), ("え", "e"), ("お", "o"),
        ("か", "ka"), ("き", "ki"), ("く", "ku"), ("け", "ke"), ("こ", "ko"),
        ("さ", "sa"), ("し", "shi"), ("す", "su"), ("せ", "se"), ("そ", "so"),
        ("た", "ta"), ("ち", "chi"), ("つ", "tsu"), ("て", "te"), ("と", "to"),
        ("な", "na"), ("に", "ni"), ("ぬ", "nu"), ("ね", "ne"), ("の", "no"),
        ("は", "ha"), ("ひ", "hi"), ("ふ", "fu"), ("へ", "he"), ("ほ", "ho"),
        ("ま", "ma"), ("み", "mi"), ("む", "mu"), ("め", "me"), ("も", "mo"),
        ("や", "ya"), ("ゆ", "yu"), ("よ", "yo"),
        ("ら", "ra"), ("り", "ri"), ("る", "ru"), ("れ", "re"), ("ろ", "ro"),
        ("わ", "wa"), ("を", "wo"), ("ん", "n"),
    ];

    private static readonly (string Kana, string Romaji)[] Dakuten =
    [
        ("が", "ga"), ("ぎ", "gi"), ("ぐ", "gu"), ("げ", "ge"), ("ご", "go"),
        ("ざ", "za"), ("じ", "ji"), ("ず", "zu"), ("ぜ", "ze"), ("ぞ", "zo"),
        ("だ", "da"), ("ぢ", "ji"), ("づ", "zu"), ("で", "de"), ("ど", "do"),
        ("ば", "ba"), ("び", "bi"), ("ぶ", "bu"), ("べ", "be"), ("ぼ", "bo"),
        ("ぱ", "pa"), ("ぴ", "pi"), ("ぷ", "pu"), ("ぺ", "pe"), ("ぽ", "po"),
    ];

    /// <summary>Only used inside words: small kana, old kana and the sounds katakana borrows for foreign words.</summary>
    private static readonly (string Kana, string Romaji)[] Extra =
    [
        ("ぁ", "a"), ("ぃ", "i"), ("ぅ", "u"), ("ぇ", "e"), ("ぉ", "o"), ("ゎ", "wa"),
        ("ゃ", "ya"), ("ゅ", "yu"), ("ょ", "yo"), ("ゐ", "wi"), ("ゑ", "we"), ("ゔ", "vu"),
        ("ふぁ", "fa"), ("ふぃ", "fi"), ("ふぇ", "fe"), ("ふぉ", "fo"), ("てぃ", "ti"), ("でぃ", "di"),
        ("とぅ", "tu"), ("どぅ", "du"), ("うぃ", "wi"), ("うぇ", "we"), ("うぉ", "wo"), ("しぇ", "she"),
        ("じぇ", "je"), ("ちぇ", "che"), ("つぁ", "tsa"), ("ゔぁ", "va"), ("ゔぃ", "vi"), ("ゔぇ", "ve"), ("ゔぉ", "vo"),
    ];

    /// <summary>Greetings that keep the old spelling: the final は is read "wa".</summary>
    private static readonly Dictionary<string, string> Words =
        new[] { ("こんにちは", "konnichiwa"), ("こんばんは", "konbanwa") }.ToDictionary(w => w.Item1, w => w.Item2, StringComparer.Ordinal);

    private static readonly IReadOnlyList<(string Kana, string Romaji)> Combos = BuildCombos();

    private static readonly Dictionary<string, string> Table =
        Basic.Concat(Dakuten).Concat(Extra).Concat(Combos)
            .GroupBy(e => e.Kana)
            .ToDictionary(g => g.Key, g => g.First().Romaji);

    /// <summary>Every kana with its katakana twin, for the drills.</summary>
    public static readonly IReadOnlyList<KanaEntry> Chart =
        Basic.Select(e => Entry(e, "basic"))
            .Concat(Dakuten.Select(e => Entry(e, "dakuten")))
            .Concat(Combos.Select(e => Entry(e, "combo")))
            .ToList();

    /// <summary>"きっぷ" -> "kippu", "ラーメン" -> "raamen". Returns null if the text has anything but kana.</summary>
    public static string? ToRomaji(string text)
    {
        var hiragana = ToHiragana(text.Trim());
        if (hiragana.Length == 0) return null;
        if (Words.TryGetValue(hiragana, out var word)) return word;

        var output = new StringBuilder();
        var doubleNext = false;
        for (var i = 0; i < hiragana.Length;)
        {
            var c = hiragana[i];
            if (c is ' ' or '\u3000') { output.Append(' '); i++; continue; }
            if (c == 'っ') { doubleNext = true; i++; continue; }
            if (c == 'ー') { output.Append(LastVowel(output)); i++; continue; }

            var length = i + 1 < hiragana.Length && Table.ContainsKey(hiragana.Substring(i, 2)) ? 2 : 1;
            if (!Table.TryGetValue(hiragana.Substring(i, length), out var romaji)) return null;
            i += length;

            if (doubleNext)
            {
                output.Append(romaji.StartsWith("ch", StringComparison.Ordinal) ? 't' : romaji[0]);
                doubleNext = false;
            }
            // ん before a vowel or y is written n' so "kin'you" is not misread as "ki-nyo".
            if (romaji == "n" && i < hiragana.Length && Table.TryGetValue(hiragana[i].ToString(), out var next)
                && next.Length > 0 && "aiueoy".Contains(next[0], StringComparison.Ordinal))
            {
                romaji = "n'";
            }
            output.Append(romaji);
        }
        return output.ToString();
    }

    /// <summary>Katakana sit exactly 0x60 code points after their hiragana twins.</summary>
    public static string ToHiragana(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= 'ァ' and <= 'ヶ' ? (char)(source[i] - 0x60) : source[i];
        });

    public static string ToKatakana(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= 'ぁ' and <= 'ゖ' ? (char)(source[i] + 0x60) : source[i];
        });

    private static KanaEntry Entry((string Kana, string Romaji) e, string group) =>
        new(e.Kana, ToKatakana(e.Kana), e.Romaji, group);

    private static char LastVowel(StringBuilder output)
    {
        for (var i = output.Length - 1; i >= 0; i--)
        {
            if ("aiueo".Contains(output[i], StringComparison.Ordinal)) return output[i];
        }
        return '-';
    }

    /// <summary>き+ゃ = きゃ (kya); し, ち and じ drop the y: sha, cha, ja.</summary>
    private static List<(string, string)> BuildCombos()
    {
        var combos = new List<(string, string)>();
        foreach (var (kana, romaji) in Basic.Concat(Dakuten))
        {
            if (!romaji.EndsWith('i') || romaji.Length < 2 || kana == "ぢ") continue;
            var stem = romaji[..^1];
            foreach (var (small, vowel) in new[] { ("ゃ", "a"), ("ゅ", "u"), ("ょ", "o") })
                combos.Add((kana + small, stem is "sh" or "ch" or "j" ? stem + vowel : stem + "y" + vowel));
        }
        return combos;
    }
}
