using ClosedXML.Excel;
using Rose.Application.Abstractions;
using Rose.Domain.Loans;
using static Rose.Infrastructure.Excel.WorkbookLayout;

namespace Rose.Infrastructure.Excel;

/// <summary>
/// loans.xlsx: a "Loans" summary sheet (one row per loan, with totals) and one sheet per loan
/// with its whole schedule. Plain numbers (no currency sign), so it fits any currency.
/// </summary>
internal sealed class LoanReportWriter : ILoanReportWriter
{
    private const string Number = "#,##0.00";
    private const string Day = "mmm d, yyyy";
    private const int MaxSheetName = 31;

    private static readonly string[] SummaryHeaders =
        ["Loan", "Type", "Loan date", "Term", "Monthly", "Loan amount", "Total payable", "Paid", "Balance", "Payments", "Next due", "Finish", "Status"];

    public byte[] Write(IReadOnlyList<LoanView> loans, DateOnly asOf)
    {
        using var workbook = new XLWorkbook();
        WriteSummary(workbook.Worksheets.Add("Loans"), loans, asOf);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Loans" };
        foreach (var loan in loans) WriteSchedule(workbook.Worksheets.Add(SheetName(loan.Name, used)), loan, asOf);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSummary(IXLWorksheet ws, IReadOnlyList<LoanView> loans, DateOnly asOf)
    {
        Title(ws, "LOAN REPORT", asOf);
        const int head = 3;
        Header(ws, head, SummaryHeaders);

        var row = head + 1;
        foreach (var loan in loans)
        {
            ws.Cell(row, 1).Value = loan.Name;
            ws.Cell(row, 2).Value = loan.Type;
            Date(ws.Cell(row, 3), loan.LoanDate);
            ws.Cell(row, 4).Value = loan.Term;
            Money(ws.Cell(row, 5), loan.MonthlyAmortization);
            Money(ws.Cell(row, 6), loan.Amount);
            Money(ws.Cell(row, 7), loan.TotalPayable);
            Money(ws.Cell(row, 8), loan.Paid);
            Money(ws.Cell(row, 9), loan.Balance);
            ws.Cell(row, 10).Value = $"{loan.PaymentsMade} of {loan.Term}";
            if (loan.NextDueDate is { } next) Date(ws.Cell(row, 11), next);
            else ws.Cell(row, 11).Value = "-";
            Date(ws.Cell(row, 12), loan.FinishDate);
            ws.Cell(row, 13).Value = StatusLabel(loan.Status);
            row++;
        }

        if (loans.Count > 0)
        {
            var total = ws.Range(row, 1, row, SummaryHeaders.Length);
            total.Style.Font.Bold = true;
            total.Style.Fill.BackgroundColor = TotalFill;
            ws.Cell(row, 1).Value = "TOTAL";
            foreach (var column in new[] { 5, 6, 7, 8, 9 })
            {
                var letter = XLHelper.GetColumnLetterFromNumber(column);
                ws.Cell(row, column).FormulaA1 = $"SUM({letter}{head + 1}:{letter}{row - 1})";
                ws.Cell(row, column).Style.NumberFormat.Format = Number;
            }
        }

        ws.Columns(1, SummaryHeaders.Length).AdjustToContents(head, row);
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 22);
        ws.SheetView.FreezeRows(head);
    }

    private static void WriteSchedule(IXLWorksheet ws, LoanView loan, DateOnly asOf)
    {
        Title(ws, $"{loan.Name.ToUpperInvariant()} ({loan.Type})", asOf);
        string[] labels = ["Loan date", "Term", "Monthly amortization", "Loan amount", "Total payable", "Paid", "Balance", "Finish"];
        var row = 3;
        foreach (var label in labels)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            var cell = ws.Cell(row, 2);
            switch (label)
            {
                case "Loan date": Date(cell, loan.LoanDate); break;
                case "Term": cell.Value = $"{loan.Term} months ({loan.PaymentsMade} paid, {loan.PaymentsLeft} left)"; break;
                case "Monthly amortization": Money(cell, loan.MonthlyAmortization); break;
                case "Loan amount": Money(cell, loan.Amount); break;
                case "Total payable": Money(cell, loan.TotalPayable); break;
                case "Paid": Money(cell, loan.Paid); break;
                case "Balance": Money(cell, loan.Balance); break;
                default: Date(cell, loan.FinishDate); break;
            }
            row++;
        }

        var head = row + 1;
        Header(ws, head, ["#", "Due date", "Amount", "Balance after", "Status"]);
        row = head + 1;
        foreach (var payment in loan.Schedule)
        {
            ws.Cell(row, 1).Value = payment.Number;
            Date(ws.Cell(row, 2), payment.DueDate);
            Money(ws.Cell(row, 3), payment.Amount);
            Money(ws.Cell(row, 4), payment.BalanceAfter);
            ws.Cell(row, 5).Value = payment.Paid ? "Paid" : "Upcoming";
            if (payment.Paid) ws.Range(row, 1, row, 5).Style.Font.FontColor = XLColor.FromHtml("#4F8A4F");
            row++;
        }

        ws.Column(1).Width = 22;
        ws.Column(2).Width = 34;
        ws.Columns(3, 5).Width = 16;
        ws.SheetView.FreezeRows(head);
    }

    private static void Title(IXLWorksheet ws, string text, DateOnly asOf)
    {
        var title = ws.Cell(1, 1);
        title.Value = text;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"As of {asOf:MMMM d, yyyy}";
        ws.Cell(2, 1).Style.Font.Italic = true;
    }

    private static void Header(IXLWorksheet ws, int row, string[] headers)
    {
        for (var c = 0; c < headers.Length; c++) ws.Cell(row, c + 1).Value = headers[c];
        var range = ws.Range(row, 1, row, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void Money(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = Number;
    }

    private static void Date(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = Day;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    private static string StatusLabel(string status) => status switch
    {
        LoanStatus.Completed => "Completed",
        LoanStatus.NotStarted => "Not started",
        _ => "Paying",
    };

    /// <summary>Excel sheet names: max 31 characters, none of : \ / ? * [ ], unique.</summary>
    internal static string SheetName(string name, HashSet<string> used)
    {
        var clean = new string([.. name.Select(ch => ":\\/?*[]".Contains(ch) ? ' ' : ch)]).Trim().Trim('\'');
        if (clean.Length == 0) clean = "Loan";
        if (clean.Length > MaxSheetName) clean = clean[..MaxSheetName].TrimEnd();
        var candidate = clean;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (clean.Length + suffix.Length > MaxSheetName ? clean[..(MaxSheetName - suffix.Length)].TrimEnd() : clean) + suffix;
        }
        return candidate;
    }
}