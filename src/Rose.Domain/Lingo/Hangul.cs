using System.Text;

namespace Rose.Domain.Lingo;

/// <summary>
/// Korean to Latin letters with the Revised Romanization, including the common sound
/// changes: linking (한국어 = hangugeo), nasalisation (합니다 = hamnida), ㄹ+ㄹ = ll and ㅎ.
/// </summary>
public static class Hangul
{
    private const char First = '\uAC00'; // 가
    private const char Last = '\uD7A3'; // 힣

    private static readonly string[] Initials =
        ["g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h"];

    private static readonly string[] Vowels =
        ["a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i"];

    /// <summary>How each final sounds at the end of a syllable.</summary>
    private static readonly string[] Finals =
        ["", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t"];

    /// <summary>A final followed by a silent ㅇ moves over: what stays and what carries on as the next sound.</summary>
    private static readonly (string Stay, string Move)[] Linked =
    [
        ("", ""), ("", "g"), ("", "kk"), ("k", "s"), ("", "n"), ("n", "j"), ("n", ""), ("", "d"), ("", "r"), ("l", "g"),
        ("l", "m"), ("l", "b"), ("l", "s"), ("l", "t"), ("l", "p"), ("", "r"), ("", "m"), ("", "b"), ("p", "s"), ("", "s"),
        ("", "ss"), ("ng", ""), ("", "j"), ("", "ch"), ("", "k"), ("", "t"), ("", "p"), ("", ""),
    ];

    private const int SilentInitial = 11; // ㅇ
    private const int RieulInitial = 5; // ㄹ
    private const int NieunInitial = 2; // ㄴ
    private const int MieumInitial = 6; // ㅁ
    private const int HieutInitial = 18; // ㅎ
    private static readonly int[] HieutFinals = [6, 15, 27]; // ㄶ ㅀ ㅎ

    /// <summary>"안녕하세요" -> "annyeonghaseyo". Returns null if the text has no Hangul at all.</summary>
    public static string? ToRomanization(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.Any(IsSyllable)) return null;

        var output = new StringBuilder();
        string? pendingInitial = null;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (!IsSyllable(c))
            {
                output.Append(c);
                pendingInitial = null;
                continue;
            }

            var (initial, vowel, final) = Split(c);
            output.Append(pendingInitial ?? Initials[initial]).Append(Vowels[vowel]);
            pendingInitial = null;

            var hasNext = i + 1 < trimmed.Length && IsSyllable(trimmed[i + 1]);
            if (!hasNext)
            {
                output.Append(Finals[final]);
                continue;
            }
            var (nextInitial, _, _) = Split(trimmed[i + 1]);
            var (stay, move) = Join(final, nextInitial);
            output.Append(stay);
            pendingInitial = move;
        }
        return output.ToString();
    }

    private static bool IsSyllable(char c) => c is >= First and <= Last;

    private static (int Initial, int Vowel, int Final) Split(char syllable)
    {
        var index = syllable - First;
        return (index / 588, index % 588 / 28, index % 28);
    }

    /// <summary>The sound of <paramref name="final"/> and of the next initial, after the sound changes.</summary>
    private static (string Stay, string? Move) Join(int final, int nextInitial)
    {
        if (final == 0) return ("", null);
        var sound = Finals[final];

        if (nextInitial == SilentInitial) return Linked[final];

        // ㅎ makes the next ㄱ ㄷ ㅈ breathy (좋다 = jota), and ㄱ ㄷ ㅂ + ㅎ do the same (축하 = chuka).
        if (HieutFinals.Contains(final))
        {
            var stay = final == 27 ? "" : sound;
            return nextInitial switch
            {
                0 => (stay, "k"),
                3 => (stay, "t"),
                12 => (stay, "ch"),
                _ => (final == 27 ? "t" : sound, null),
            };
        }
        if (nextInitial == HieutInitial && sound is "k" or "t" or "p") return ("", sound);

        if (nextInitial is NieunInitial or MieumInitial)
        {
            return sound switch
            {
                "k" => ("ng", null),
                "t" => ("n", null),
                "p" => ("m", null),
                "l" when nextInitial == NieunInitial => ("l", "l"),
                _ => (sound, null),
            };
        }
        if (nextInitial == RieulInitial && sound is "l" or "n") return ("l", "l");
        return (sound, null);
    }
}
