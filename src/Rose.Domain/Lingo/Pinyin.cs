using System.Collections.Frozen;
using System.Text;

namespace Rose.Domain.Lingo;

/// <summary>
/// Chinese characters to pinyin with tone marks: "你好" -> "nǐ hǎo". Each character gets its most
/// common reading, so a few words that change reading by context (行 in 银行 = háng) come out wrong;
/// you can always edit the reading on the card.
/// </summary>
public static class Pinyin
{
    // Pinyin.txt is compiled into the DLL (an embedded resource), so there is no file to ship or lose.
    private static readonly Lazy<FrozenDictionary<char, string>> Table = new(Load);

    /// <summary>The reading, or null if the text has no Chinese characters at all.</summary>
    public static string? ToPinyin(string text)
    {
        var table = Table.Value;
        var words = new List<string>();
        var other = new StringBuilder();
        var found = false;

        foreach (var c in text)
        {
            if (table.TryGetValue(c, out var reading))
            {
                Flush();
                words.Add(reading);
                found = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                other.Append(c);
            }
        }
        Flush();
        return found ? string.Join(' ', words) : null;

        void Flush()
        {
            if (other.Length == 0) return;
            words.Add(other.ToString());
            other.Clear();
        }
    }

    private static FrozenDictionary<char, string> Load()
    {
        using var stream = typeof(Pinyin).Assembly.GetManifestResourceStream("Rose.Domain.Lingo.Pinyin.txt")
            ?? throw new InvalidOperationException("The Pinyin.txt resource is missing from Rose.Domain.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var table = new Dictionary<char, string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var reading = line[..tab];
            foreach (var c in line.AsSpan(tab + 1)) table[c] = reading;
        }
        return table.ToFrozenDictionary();
    }
}
