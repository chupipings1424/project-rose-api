using Rose.Domain.Common;

namespace Rose.Domain.Budget;

/// <summary>One row of the Budget sheet: an item with twelve monthly amounts.</summary>
public sealed record BudgetLine(
    string Id,
    int Year,
    string Section,
    string Group,
    string Item,
    double[] Amounts,
    double Total,
    string Notes)
{
    public bool Matches(int year, string section, string group, string item) =>
        Year == year
        && Section == section
        && string.Equals(Group, group, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Item, item, StringComparison.OrdinalIgnoreCase);
}

public static class BudgetRules
{
    public const string Revenue = "Revenue";
    public const string Expense = "Expense";

    public static readonly IReadOnlyList<string> Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static readonly IReadOnlyList<string> Sections = [Revenue, Expense];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultGroups =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Revenue] = ["Income"],
            [Expense] = ["Home", "Daily Living", "Transportation", "Health", "Other"],
        };

    /// <summary>"revenue", "Expenses", " EXPENSE " -> the canonical section name.</summary>
    public static string CanonicalSection(object? section)
    {
        var key = Coerce.ToText(section).Trim().ToLowerInvariant();
        foreach (var name in Sections)
        {
            var lower = name.ToLowerInvariant();
            if (key == lower || key.TrimEnd('s') == lower) return name;
        }
        throw new ValidationException($"Section must be one of {string.Join(", ", Sections)}.");
    }

    /// <summary>Exactly twelve rounded amounts from whatever was given (missing months are 0).</summary>
    public static double[] CleanAmounts(IEnumerable<object?>? values)
    {
        var amounts = new double[12];
        if (values is null) return amounts;
        var i = 0;
        foreach (var value in values)
        {
            if (i == 12) break;
            amounts[i++] = Coerce.Round(Coerce.ToDouble(value));
        }
        return amounts;
    }

    /// <summary>0-11, or a month name like "Mar" / "march".</summary>
    public static int MonthIndex(string? month)
    {
        int index;
        var text = (month ?? "").Trim();
        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            var key = Coerce.TitleCase(text[..Math.Min(3, text.Length)]);
            index = Months.ToList().IndexOf(key);
            if (index < 0) throw new ValidationException($"Unknown month '{month}'.");
        }
        else
        {
            index = Coerce.ToInt(month, -1);
        }
        if (index is < 0 or > 11) throw new ValidationException("Month must be between 0 and 11.");
        return index;
    }

    public static string DuplicateMessage(string item, string group, int year) =>
        $"'{item}' already exists under {group} for {year}.";
}