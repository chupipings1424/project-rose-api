using Rose.Application.Abstractions;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Application.Importing;

/// <summary>
/// Works out what importing a file would do, row by row, without touching the workbook.
/// Pure: given the file's rows and what is already saved, it returns a plan.
/// </summary>
public static class ImportPlanner
{
    public const int MaxRows = 5000;
    public static readonly IReadOnlyList<string> Modes = ["skip", "replace"];

    private static readonly Dictionary<string, int> MonthAliases = new()
    {
        ["jan"] = 0, ["january"] = 0, ["feb"] = 1, ["february"] = 1, ["mar"] = 2, ["march"] = 2,
        ["apr"] = 3, ["april"] = 3, ["may"] = 4, ["jun"] = 5, ["june"] = 5, ["jul"] = 6, ["july"] = 6,
        ["aug"] = 7, ["august"] = 7, ["sep"] = 8, ["sept"] = 8, ["september"] = 8,
        ["oct"] = 9, ["october"] = 9, ["nov"] = 10, ["november"] = 10, ["dec"] = 11, ["december"] = 11,
    };

    private static readonly (int Days, string Name)[] FrequencyDays =
        [(7, "Weekly"), (14, "Bi-weekly"), (31, "Monthly"), (92, "Quarterly"), (366, "Yearly")];

    /// <summary>Header normalisation: " Due_Date " -> "due date".</summary>
    public static string Key(object? name) =>
        Coerce.ToText(name).Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');

    public static string NormaliseMode(string? mode) => Modes.Contains(mode) ? mode! : "skip";

    // ---------------------------------------------------------------- budget

    public static ImportPlan AnalyzeBudget(
        IReadOnlyList<UploadSheet> sheets, string? defaultYear, string? mode, IReadOnlyList<BudgetLine> existingLines, int currentYear)
    {
        mode = NormaliseMode(mode);
        var rows = Pick(sheets, "Budget") is { Count: > 0 } picked ? picked : First(sheets);
        if (rows.Count > MaxRows) throw new ValidationException($"That file has {rows.Count} rows; the limit is {MaxRows}.");

        var fallbackYear = Coerce.ToInt(defaultYear, currentYear);
        if (fallbackYear == 0) fallbackYear = currentYear;
        var existing = new Dictionary<(int, string, string, string), string>();
        foreach (var line in existingLines)
            existing.TryAdd((line.Year, line.Section.ToLowerInvariant(), line.Group.ToLowerInvariant(), line.Item.ToLowerInvariant()), line.Id);
        var seen = new HashSet<(int, string, string, string)>();

        var entries = new List<ImportEntry>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var item = Coerce.ToText(Cell(row, "item", "line", "description", "name")).Trim();
            var entry = new ImportEntry { Row = i + 2, Item = item };
            entries.Add(entry);

            if (item.Length == 0)
            {
                entry.Fail("No item name in this row.");
                continue;
            }

            string section;
            try
            {
                section = BudgetRules.CanonicalSection(CellOr(row, "Expense", "section", "type"));
            }
            catch (ValidationException)
            {
                entry.Fail("Section must be Revenue or Expense.");
                continue;
            }

            var group = Coerce.ToText(CellOr(row, "", "group", "category")).Trim();
            if (group.Length == 0) group = BudgetRules.DefaultGroups[section][0];
            var year = Coerce.ToInt(CellOr(row, fallbackYear, "year"), fallbackYear);
            if (year == 0) year = fallbackYear;
            var amounts = new double[12];
            foreach (var (header, value) in row)
                if (MonthAliases.TryGetValue(Key(header), out var month)) amounts[month] = Coerce.Round(Coerce.ToDouble(value));

            entry.Section = section;
            entry.Group = group;
            entry.Year = year;
            entry.Amounts = amounts;
            entry.Total = Coerce.Round(amounts.Sum());
            entry.Notes = Coerce.ToText(CellOr(row, "", "notes", "note", "comment")).Trim();

            var key = (year, section.ToLowerInvariant(), group.ToLowerInvariant(), item.ToLowerInvariant());
            if (seen.Contains(key))
            {
                entry.Fail("This line is repeated earlier in the file.");
            }
            else if (existing.TryGetValue(key, out var existingId))
            {
                entry.ExistingId = existingId;
                (entry.Action, entry.Reason) = mode == "replace"
                    ? (ImportAction.Replace, "Already in the budget; amounts will be overwritten.")
                    : (ImportAction.Skip, "Already in the budget.");
            }
            else
            {
                entry.Action = ImportAction.Add;
            }
            seen.Add(key);
        }

        return ImportPlan.Summarize("budget", entries, mode);
    }

    // ---------------------------------------------------------------- payments

    public static ImportPlan AnalyzePayments(
        IReadOnlyList<UploadSheet> sheets, string? mode, IReadOnlyList<Account> existingAccounts, IReadOnlyList<Payment> existingPayments)
    {
        mode = NormaliseMode(mode);
        var accountRows = Pick(sheets, "Accounts", "Titles", "Plans");
        var paymentRows = Pick(sheets, "Payments", "Transactions");
        if (paymentRows.Count == 0 && accountRows.Count == 0) paymentRows = First(sheets);
        if (paymentRows.Count + accountRows.Count > MaxRows)
            throw new ValidationException($"That file has too many rows; the limit is {MaxRows}.");

        var existingTitles = existingAccounts.Select(a => a.Title.ToLowerInvariant()).ToHashSet();
        var existingKeys = existingPayments
            .Select(p => (p.Title.ToLowerInvariant(), p.Date, Coerce.Round(p.Amount), p.Note.Trim().ToLowerInvariant()))
            .ToHashSet();

        var entries = new List<ImportEntry>();
        var plannedTitles = new HashSet<string>();

        for (var i = 0; i < accountRows.Count; i++)
        {
            var row = accountRows[i];
            var title = Coerce.ToText(Cell(row, "title", "name", "account")).Trim();
            var entry = new ImportEntry { Row = i + 2, Kind = "account", Item = title };
            entries.Add(entry);
            if (title.Length == 0)
            {
                entry.Fail("No title in this row.");
                continue;
            }

            entry.TotalAmount = Coerce.Round(Coerce.ToDouble(Cell(row, "total amount", "total", "amount")));
            entry.StartDate = IsoOrNull(CellOr(row, null, "start date", "start"));
            entry.DueDate = IsoOrNull(CellOr(row, null, "due date", "due"));
            entry.Frequency = Schedule.CanonicalFrequency(Coerce.ToText(CellOr(row, "Monthly", "frequency")));
            entry.Instalments = Coerce.ToInt(CellOr(row, 0, "instalments", "installments", "payments"));
            entry.PaymentAmount = Coerce.Round(Coerce.ToDouble(CellOr(row, 0, "payment amount", "per payment")));
            entry.Notes = Coerce.ToText(CellOr(row, "", "notes", "note")).Trim();

            var key = title.ToLowerInvariant();
            if (plannedTitles.Contains(key)) entry.Fail("This title is repeated earlier in the file.");
            else if (existingTitles.Contains(key)) (entry.Action, entry.Reason) = (ImportAction.Skip, "A title with this name already exists.");
            else
            {
                entry.Action = ImportAction.Add;
                plannedTitles.Add(key);
            }
        }

        var seen = new HashSet<(string, string, double, string)>();
        var byTitle = new List<(string Title, List<ImportEntry> Rows)>();

        for (var i = 0; i < paymentRows.Count; i++)
        {
            var row = paymentRows[i];
            var title = Coerce.ToText(Cell(row, "title", "account", "name")).Trim();
            var entry = new ImportEntry { Row = i + 2, Kind = "payment", Item = title };
            entries.Add(entry);

            var when = Coerce.ToDate(CellOr(row, null, "date", "paid date", "payment date"));
            var amount = Coerce.Round(Coerce.ToDouble(CellOr(row, 0, "amount", "value", "paid")));
            var note = Coerce.ToText(CellOr(row, "", "note", "notes", "description")).Trim();
            var statusText = Coerce.TitleCase(Coerce.ToText(CellOr(row, "Paid", "status")).Trim());

            if (title.Length == 0) { entry.Fail("No title in this row."); continue; }
            if (when is null) { entry.Fail("Missing or unreadable date."); continue; }
            if (amount <= 0) { entry.Fail("Amount must be greater than zero."); continue; }

            var date = Coerce.IsoDate(when.Value);
            entry.Date = date;
            entry.Amount = amount;
            entry.Status = PaymentStatus.IsKnown(statusText) ? statusText : PaymentStatus.Paid;
            var time = Coerce.ToTimeText(CellOr(row, "09:00", "time"));
            entry.Time = time.Length > 0 ? time : "9:00am";
            var due = Coerce.Iso(CellOr(row, null, "due date", "due"));
            entry.DueDate = due.Length > 0 ? due : date;
            entry.Note = note;

            var key = (title.ToLowerInvariant(), date, amount, note.ToLowerInvariant());
            if (seen.Contains(key)) entry.Fail("This payment is repeated earlier in the file.");
            else if (existingKeys.Contains(key) && mode == "skip") (entry.Action, entry.Reason) = (ImportAction.Skip, "An identical payment is already recorded.");
            else
            {
                entry.Action = ImportAction.Add;
                var index = byTitle.FindIndex(t => t.Title == title);
                if (index < 0) byTitle.Add((title, [entry]));
                else byTitle[index].Rows.Add(entry);
            }
            seen.Add(key);
        }

        // A title with payments but no account (in the file or the workbook) gets one derived from its payments.
        foreach (var (title, rows) in byTitle)
        {
            var key = title.ToLowerInvariant();
            if (existingTitles.Contains(key) || plannedTitles.Contains(key)) continue;
            var dates = rows.Select(r => DateOnly.Parse(r.Date!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var amounts = rows.Select(r => r.Amount!.Value).ToList();
            entries.Add(new ImportEntry
            {
                Row = 0,
                Kind = "account",
                Item = title,
                Action = ImportAction.Add,
                Reason = "Created automatically from the imported payments.",
                Derived = true,
                TotalAmount = Coerce.Round(amounts.Sum()),
                StartDate = Coerce.IsoDate(dates.Min()),
                DueDate = Coerce.IsoDate(dates.Max()),
                Frequency = GuessFrequency(dates),
                Instalments = rows.Count,
                PaymentAmount = MostCommon(amounts),
                Notes = "Imported",
            });
            plannedTitles.Add(key);
        }

        return ImportPlan.Summarize("payments", entries, mode);
    }

    /// <summary>The median gap between dates mapped to a frequency name.</summary>
    public static string GuessFrequency(IEnumerable<DateOnly> dates)
    {
        var unique = dates.Distinct().Order().ToList();
        if (unique.Count < 2) return "Monthly";
        var gaps = unique.Zip(unique.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Where(g => g > 0).Order().ToList();
        if (gaps.Count == 0) return "Monthly";
        var median = gaps[gaps.Count / 2];
        foreach (var (days, name) in FrequencyDays)
            if (median <= days) return name;
        return "Yearly";
    }

    /// <summary>The most frequent value; ties go to the one seen first (like Python's Counter.most_common).</summary>
    private static double MostCommon(List<double> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

    // ---------------------------------------------------------------- helpers

    private static string? IsoOrNull(object? value) => Coerce.ToDate(value) is { } date ? Coerce.IsoDate(date) : null;

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Pick(IReadOnlyList<UploadSheet> sheets, params string[] wanted)
    {
        foreach (var target in wanted)
            foreach (var sheet in sheets)
                if (Key(sheet.Name) == Key(target)) return sheet.Rows;
        return [];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> First(IReadOnlyList<UploadSheet> sheets) =>
        sheets.Count > 0 ? sheets[0].Rows : [];

    /// <summary>The first non-empty value among the given column names, else "".</summary>
    private static object? Cell(IReadOnlyDictionary<string, object?> row, params string[] names) => CellOr(row, "", names);

    private static object? CellOr(IReadOnlyDictionary<string, object?> row, object? fallback, params string[] names)
    {
        foreach (var name in names)
            if (row.TryGetValue(name, out var value) && !Coerce.IsBlank(value)) return value;
        return fallback;
    }
}