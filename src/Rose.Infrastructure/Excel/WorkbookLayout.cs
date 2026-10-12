using ClosedXML.Excel;

namespace Rose.Infrastructure.Excel;

/// <summary>Sheet names, columns and styles - identical to the original Python tracker.</summary>
internal static class WorkbookLayout
{
    public const string AccountsSheet = "Accounts";
    public const string PaymentsSheet = "Payments";
    public const string BudgetSheet = "Budget";
    public const string ReportPrefix = "Budget ";
    public const string LegacyReportSheet = "Budget Report";

    public const string MoneyFormat = "\"$\"#,##0.00";
    public const string DateFormat = "dddd, mmmm d, yyyy";

    public static readonly string[] AccountHeaders =
        ["Title", "Total Amount", "Start Date", "Due Date", "Frequency", "Instalments", "Payment Amount", "Notes"];

    public static readonly string[] PaymentHeaders = ["ID", "Title", "Amount", "Date", "Time", "Status", "Due Date", "Note"];

    public static readonly int[] AccountWidths = [26, 16, 30, 30, 14, 13, 17, 34];
    public static readonly int[] PaymentWidths = [38, 26, 16, 30, 12, 12, 30, 34];

    // Budget sheet: ID | Year | Section | Group | Item | Jan .. Dec | Notes
    public const int ColId = 1, ColYear = 2, ColSection = 3, ColGroup = 4, ColItem = 5;
    public const int FirstMonthCol = 6, LastMonthCol = 17, ColNotes = 18;

    public static readonly XLColor HeaderFill = XLColor.FromHtml("#A9D08E");
    public static readonly XLColor BannerFill = XLColor.FromHtml("#4F8A4F");
    public static readonly XLColor GroupFill = XLColor.FromHtml("#D9E8D0");
    public static readonly XLColor TotalFill = XLColor.FromHtml("#F0F4F0");
    public static readonly XLColor RuleColor = XLColor.FromHtml("#DFE4DF");

    /// <summary>Bold green header row, column widths, frozen header and an autofilter.</summary>
    public static void StyleHeader(IXLWorksheet ws, IReadOnlyList<string> headers, IReadOnlyList<int> widths, int frozenColumns)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        for (var i = 0; i < widths.Count; i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.Freeze(1, frozenColumns);
        ws.Range(1, 1, 1, headers.Count).SetAutoFilter();
    }
}