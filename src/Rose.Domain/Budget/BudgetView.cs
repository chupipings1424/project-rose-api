using System.Text.Json.Serialization;
using Rose.Domain.Common;

namespace Rose.Domain.Budget;

/// <summary>A year of the budget, grouped and totalled the way the budget page shows it.</summary>
public sealed record BudgetView(
    int Year,
    IReadOnlyList<string> Months,
    IReadOnlyList<BudgetSectionView> Sections,
    KnownGroups KnownGroups,
    double TotalRevenue,
    double TotalExpenses,
    double Net,
    double[] NetByMonth,
    int LineCount,
    IReadOnlyList<int> Years)
{
    public BudgetSectionView Section(string name) => Sections.First(s => s.Name == name);

    /// <summary>Every year with lines, plus the current year (which always exists, maybe empty).</summary>
    public static List<int> YearsOf(IEnumerable<BudgetLine> lines, int currentYear) =>
        lines.Select(l => l.Year).Append(currentYear).Distinct().Order().ToList();

    public static BudgetView Build(int year, IReadOnlyList<BudgetLine> allLines, int currentYear)
    {
        var lines = allLines.Where(l => l.Year == year).ToList();
        var sections = new List<BudgetSectionView>();
        var netByMonth = new double[12];

        foreach (var section in BudgetRules.Sections)
        {
            var sectionLines = lines.Where(l => l.Section == section).ToList();
            var groups = new List<(string Name, List<BudgetLine> Lines, double[] Monthly)>();
            foreach (var line in sectionLines)
            {
                var index = groups.FindIndex(g => g.Name == line.Group);
                if (index < 0)
                {
                    groups.Add((line.Group, [], new double[12]));
                    index = groups.Count - 1;
                }
                groups[index].Lines.Add(line);
                for (var m = 0; m < 12; m++) groups[index].Monthly[m] += line.Amounts[m];
            }

            var monthly = new double[12];
            var groupViews = new List<BudgetGroupView>();
            foreach (var (name, groupLines, raw) in groups)
            {
                var rounded = raw.Select(v => Coerce.Round(v)).ToArray();
                for (var m = 0; m < 12; m++) monthly[m] += rounded[m];
                groupViews.Add(new BudgetGroupView(name, groupLines, rounded, Coerce.Round(rounded.Sum())));
            }

            var sign = section == BudgetRules.Revenue ? 1 : -1;
            for (var m = 0; m < 12; m++) netByMonth[m] += sign * monthly[m];

            sections.Add(new BudgetSectionView(
                section,
                section == BudgetRules.Revenue ? "Revenue" : "Expenses",
                groupViews,
                monthly.Select(v => Coerce.Round(v)).ToArray(),
                Coerce.Round(monthly.Sum())));
        }

        string[] Known(string section)
        {
            var names = lines.Where(l => l.Section == section && l.Group.Length > 0)
                .Select(l => l.Group).Distinct().Order(StringComparer.Ordinal).ToArray();
            return names.Length > 0 ? names : [.. BudgetRules.DefaultGroups[section]];
        }

        var revenue = sections[0].Total;
        var expense = sections[1].Total;
        return new BudgetView(
            year,
            BudgetRules.Months,
            sections,
            new KnownGroups(Known(BudgetRules.Revenue), Known(BudgetRules.Expense)),
            revenue,
            expense,
            Coerce.Round(revenue - expense),
            netByMonth.Select(v => Coerce.Round(v)).ToArray(),
            lines.Count,
            YearsOf(allLines, currentYear));
    }
}

public sealed record BudgetSectionView(
    string Name, string Label, IReadOnlyList<BudgetGroupView> Groups, double[] Monthly, double Total);

public sealed record BudgetGroupView(string Name, IReadOnlyList<BudgetLine> Lines, double[] Monthly, double Total);

/// <summary>Group names used so far per section (the keys keep their capitals in JSON).</summary>
public sealed record KnownGroups(
    [property: JsonPropertyName("Revenue")] IReadOnlyList<string> Revenue,
    [property: JsonPropertyName("Expense")] IReadOnlyList<string> Expense);