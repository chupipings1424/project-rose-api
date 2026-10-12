using System.Globalization;

namespace Rose.Domain.Common;

/// <summary>
/// Turns loosely typed values (Excel cells, form fields, CSV text) into clean ones.
/// The rules deliberately match the original Python tracker so existing workbooks
/// and imports behave exactly the same.
/// </summary>
public static class Coerce
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly string[] DateFormats =
        ["yyyy-M-d", "M/d/yyyy", "d/M/yyyy", "dddd, MMMM d, yyyy", "MMMM d, yyyy"];

    private static readonly string[] TimeFormats = ["H:m", "H:m:s", "h:mtt"];

    public static bool IsBlank(object? value) => value is null || value is string { Length: 0 };

    /// <summary>The text Python's <c>str()</c> would give (integral numbers without ".0").</summary>
    public static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        double d when double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) < 1e15 => d.ToString("0", Invariant),
        double d => d.ToString("R", Invariant),
        int i => i.ToString(Invariant),
        long l => l.ToString(Invariant),
        decimal m => m.ToString(Invariant),
        bool b => b ? "True" : "False",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", Invariant),
        DateOnly d => d.ToString("yyyy-MM-dd", Invariant),
        _ => Convert.ToString(value, Invariant) ?? "",
    };

    public static DateOnly? ToDate(object? value) => value switch
    {
        null => null,
        string { Length: 0 } => null,
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.DateTime),
        _ => DateOnly.TryParseExact(ToText(value).Trim(), DateFormats, Invariant, DateTimeStyles.None, out var parsed)
            ? parsed
            : null,
    };

    /// <summary>ISO date text ("2024-03-01") or "" when the value is not a date.</summary>
    public static string Iso(object? value) => ToDate(value) is { } d ? IsoDate(d) : "";

    public static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", Invariant);

    /// <summary>Normalises a time to a friendly "2:15pm"; unknown text is returned as typed.</summary>
    public static string ToTimeText(object? value)
    {
        switch (value)
        {
            case null or string { Length: 0 }:
                return "";
            case DateTime dt:
                return FormatTime(dt.Hour, dt.Minute);
            case TimeSpan ts:
                return FormatTime(ts.Hours, ts.Minutes);
            case TimeOnly t:
                return FormatTime(t.Hour, t.Minute);
        }

        var text = ToText(value).Trim();
        var compact = text.ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
        return DateTime.TryParseExact(compact, TimeFormats, Invariant, DateTimeStyles.None, out var parsed)
            ? FormatTime(parsed.Hour, parsed.Minute)
            : text;
    }

    private static string FormatTime(int hour, int minute)
    {
        var twelve = hour % 12 == 0 ? 12 : hour % 12;
        return $"{twelve}:{minute:00}{(hour < 12 ? "am" : "pm")}";
    }

    /// <summary>Money and amounts: "$1,200.50" -> 1200.5; anything unreadable -> 0.</summary>
    public static double ToDouble(object? value)
    {
        var result = value switch
        {
            null or string { Length: 0 } => 0,
            double d => d,
            int i => i,
            long l => l,
            decimal m => (double)m,
            bool b => b ? 1 : 0,
            _ => double.TryParse(
                ToText(value).Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim(),
                NumberStyles.Float, Invariant, out var parsed) ? parsed : 0,
        };
        return double.IsFinite(result) ? result : 0;
    }

    /// <summary>Whole numbers: "12", 12.0 and "12.7" all give 12 (truncated, like Python's int(float(x))).</summary>
    public static int ToInt(object? value, int fallback = 0)
    {
        if (IsBlank(value) || value is bool) return fallback;
        if (!double.TryParse(ToText(value).Trim(), NumberStyles.Float, Invariant, out var parsed)) return fallback;
        var truncated = Math.Truncate(parsed);
        return truncated is >= int.MinValue and <= int.MaxValue ? (int)truncated : fallback;
    }

    /// <summary>Python's round(x, n): banker's rounding.</summary>
    public static double Round(double value, int digits = 2) => Math.Round(value, digits, MidpointRounding.ToEven);

    /// <summary>Python's str.title(): "unpaid" -> "Unpaid", "PAID" -> "Paid".</summary>
    public static string TitleCase(string text)
    {
        var chars = text.ToCharArray();
        var previousIsLetter = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var isLetter = char.IsLetter(chars[i]);
            chars[i] = isLetter && !previousIsLetter ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
            previousIsLetter = isLetter;
        }
        return new string(chars);
    }
}