using ClosedXML.Excel;
using Rose.Domain.Budget;
using static Rose.Infrastructure.Excel.WorkbookLayout;

namespace Rose.Infrastructure.Excel;

/// <summary>
/// The generated "Budget YYYY" sheets: the classic REVENUE / EXPENSES month grid with live
/// SUM formulas. Rewritten from scratch on every budget save - never edit them by hand.
/// </summary>
internal static class BudgetReportWriter
{
    private const int LastColumn = 14;

    public static void RebuildAll(XLWorkbook workbook, IReadOnlyList<BudgetLine> lines)
    {
        if (workbook.TryGetWorksheet(LegacyReportSheet, out var legacy)) legacy.Delete();

        var byYear = lines.GroupBy(l => l.Year).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var ws in workbook.Worksheets.ToList())
        {
            if (!ws.Name.StartsWith(ReportPrefix, StringComparison.Ordinal)) continue;
            var suffix = ws.Name[ReportPrefix.Length..];
            if (suffix.Length == 0 || !suffix.All(char.IsAsciiDigit) || !int.TryParse(suffix, out var year) || !byYear.ContainsKey(year))
                ws.Delete();
        }

        foreach (var year in byYear.Keys.Order()) Write(workbook, year, byYear[year]);

        // Keep the year sheets in order after the data sheets.
        var data = workbook.Worksheets.Where(ws => !ws.Name.StartsWith(ReportPrefix, StringComparison.Ordinal)).ToList();
        var reports = workbook.Worksheets
            .Where(ws => ws.Name.StartsWith(ReportPrefix, StringComparison.Ordinal))
            .OrderBy(ws => int.Parse(ws.Name[ReportPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var position = 1;
        foreach (var ws in data.Concat(reports)) ws.Position = position++;
    }

    private static void Write(XLWorkbook workbook, int year, List<BudgetLine> lines)
    {
        var name = ReportPrefix + year;
        if (workbook.TryGetWorksheet(name, out var old)) old.Delete();
        var ws = workbook.Worksheets.Add(name);

        ws.Column(1).Width = 30;
        for (var c = 2; c <= 13; c++) ws.Column(c).Width = 12;
        ws.Column(14).Width = 14;

        var heading = ws.Cell(1, 1);
        heading.Value = $"PERSONAL BUDGET {year}";
        heading.Style.Font.Bold = true;
        heading.Style.Font.FontSize = 14;

        var row = 3;
        var sectionTotalRows = new Dictionary<string, int>();

        foreach (var section in BudgetRules.Sections)
        {
            var bannerLabel = section == "Revenue" ? "REVENUE" : "EXPENSES";
            Fill(ws, row, BannerFill);
            var banner = ws.Cell(row, 1);
            banner.Value = bannerLabel;
            BannerFont(banner, 12);
            row++;

            var groupTotalRows = new List<int>();
            var sectionLines = lines.Where(l => l.Section == section).ToList();
            var groups = sectionLines.Select(l => l.Group).Distinct().ToList();
            if (groups.Count == 0) groups = [BudgetRules.DefaultGroups[section][0]];

            foreach (var group in groups)
            {
                Fill(ws, row, GroupFill);
                Bold(ws.Cell(row, 1), (group.Length > 0 ? group : "Uncategorised").ToUpperInvariant());
                for (var m = 0; m < 12; m++)
                {
                    var cell = ws.Cell(row, 2 + m);
                    Bold(cell, BudgetRules.Months[m].ToUpperInvariant());
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                Bold(ws.Cell(row, 14), "TOTAL");
                row++;

                var firstItemRow = row;
                foreach (var line in sectionLines.Where(l => l.Group == group))
                {
                    var item = ws.Cell(row, 1);
                    if (line.Item.Length > 0) item.Value = line.Item;
                    Rule(item);
                    for (var m = 0; m < 12; m++)
                    {
                        var cell = ws.Cell(row, 2 + m);
                        cell.Value = line.Amounts[m];
                        Money(cell);
                        Rule(cell);
                    }
                    var total = ws.Cell(row, 14);
                    total.FormulaA1 = $"SUM(B{row}:M{row})";
                    Money(total);
                    Rule(total);
                    row++;
                }
                var lastItemRow = row - 1;

                Fill(ws, row, TotalFill);
                Bold(ws.Cell(row, 1), "Total");
                for (var c = 2; c <= LastColumn; c++)
                {
                    var cell = ws.Cell(row, c);
                    var letter = XLHelper.GetColumnLetterFromNumber(c);
                    if (lastItemRow >= firstItemRow) cell.FormulaA1 = $"SUM({letter}{firstItemRow}:{letter}{lastItemRow})";
                    else cell.Value = 0;
                    Money(cell);
                    cell.Style.Font.Bold = true;
                }
                groupTotalRows.Add(row);
                row += 2;
            }

            Fill(ws, row, TotalFill);
            Bold(ws.Cell(row, 1), $"TOTAL {bannerLabel}");
            for (var c = 2; c <= LastColumn; c++)
            {
                var letter = XLHelper.GetColumnLetterFromNumber(c);
                var cell = ws.Cell(row, c);
                cell.FormulaA1 = groupTotalRows.Count > 0 ? string.Join("+", groupTotalRows.Select(r => $"{letter}{r}")) : "0";
                Money(cell);
                cell.Style.Font.Bold = true;
            }
            sectionTotalRows[section] = row;
            row += 2;
        }

        Fill(ws, row, BannerFill);
        var net = ws.Cell(row, 1);
        net.Value = "NET (REVENUE - EXPENSES)";
        BannerFont(net, 12);
        for (var c = 2; c <= LastColumn; c++)
        {
            var letter = XLHelper.GetColumnLetterFromNumber(c);
            var cell = ws.Cell(row, c);
            cell.FormulaA1 = $"{letter}{sectionTotalRows["Revenue"]}-{letter}{sectionTotalRows["Expense"]}";
            Money(cell);
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
        }

        ws.SheetView.Freeze(3, 1);
    }

    private static void Fill(IXLWorksheet ws, int row, XLColor color) =>
        ws.Range(row, 1, row, LastColumn).Style.Fill.BackgroundColor = color;

    private static void Bold(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
    }

    private static void BannerFont(IXLCell cell, double size)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Font.FontSize = size;
    }

    private static void Money(IXLCell cell) => cell.Style.NumberFormat.Format = MoneyFormat;

    private static void Rule(IXLCell cell)
    {
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorderColor = RuleColor;
    }
}