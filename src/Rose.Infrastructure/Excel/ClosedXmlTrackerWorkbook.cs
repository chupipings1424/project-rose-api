using ClosedXML.Excel;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;
using static Rose.Infrastructure.Excel.WorkbookLayout;

namespace Rose.Infrastructure.Excel;

/// <summary>One open copy of the workbook. Changes stay in memory until <see cref="Save"/>.</summary>
internal sealed class ClosedXmlTrackerWorkbook(XLWorkbook workbook, string path, TimeProvider clock, Action release)
    : ITrackerWorkbook
{
    private List<Account>? accounts;
    private List<Payment>? payments;
    private List<BudgetLine>? budgetLines;
    private bool budgetChanged;
    private bool disposed;

    private int CurrentYear => clock.Today().Year;

    private IXLWorksheet AccountsSheet => workbook.Worksheet(WorkbookLayout.AccountsSheet);
    private IXLWorksheet PaymentsSheet => workbook.Worksheet(WorkbookLayout.PaymentsSheet);

    // ------------------------------------------------------------------ reads

    /// <summary>Maps columns by header name so older layouts still load. The title is always column A.</summary>
    public IReadOnlyList<Account> Accounts()
    {
        if (accounts is not null) return accounts;
        var ws = AccountsSheet;
        var index = new Dictionary<string, int>();
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= lastColumn; c++)
        {
            var name = CellValues.Read(ws.Cell(1, c));
            if (!Coerce.IsBlank(name)) index[Coerce.ToText(name).Trim().ToLowerInvariant()] = c;
        }

        object? Cell(int row, string header, object? fallback = null)
        {
            if (!index.TryGetValue(header, out var column)) return fallback;
            var value = CellValues.Read(ws.Cell(row, column));
            return Coerce.IsBlank(value) ? fallback : value;
        }

        accounts = [];
        foreach (var row in DataRows(ws))
        {
            var title = CellValues.Read(ws.Cell(row, 1));
            if (CellValues.IsFalsy(title)) continue;
            var frequency = Cell(row, "frequency", "");
            accounts.Add(new Account(
                Coerce.ToText(title).Trim(),
                Coerce.ToDouble(Cell(row, "total amount", 0d)),
                Coerce.Iso(Cell(row, "start date")),
                Coerce.Iso(Cell(row, "due date")),
                frequency is string s ? s.Trim() : Coerce.ToText(frequency),
                Coerce.ToInt(Cell(row, "instalments", 0d)),
                Coerce.ToDouble(Cell(row, "payment amount", 0d)),
                CellValues.Text(Cell(row, "notes", ""))));
        }
        return accounts;
    }

    public IReadOnlyList<Payment> Payments()
    {
        if (payments is not null) return payments;
        var ws = PaymentsSheet;
        var list = new List<Payment>();
        foreach (var row in DataRows(ws))
        {
            object? At(int column) => CellValues.Read(ws.Cell(row, column));
            var title = At(2);
            if (CellValues.IsFalsy(title)) continue;
            var status = At(6);
            list.Add(new Payment(
                CellValues.Text(At(1)),
                Coerce.ToText(title).Trim(),
                Coerce.ToDouble(At(3)),
                Coerce.Iso(At(4)),
                Coerce.ToTimeText(At(5)),
                CellValues.IsFalsy(status) ? PaymentStatus.Unpaid : Coerce.ToText(status).Trim(),
                Coerce.Iso(At(7)),
                CellValues.Text(At(8))));
        }
        // Same order as Python: by date text, then time text (stable).
        payments = [.. list.OrderBy(p => p.Date, StringComparer.Ordinal).ThenBy(p => p.Time, StringComparer.Ordinal)];
        return payments;
    }

    public IReadOnlyList<BudgetLine> BudgetLines()
    {
        if (budgetLines is not null) return budgetLines;
        budgetLines = [];
        if (!workbook.TryGetWorksheet(BudgetSheet, out var ws)) return budgetLines;
        foreach (var row in DataRows(ws))
            if (ReadLine(ws, row) is { } line) budgetLines.Add(line);
        return budgetLines;
    }

    private BudgetLine? ReadLine(IXLWorksheet ws, int row)
    {
        object? At(int column) => CellValues.Read(ws.Cell(row, column));
        var id = At(ColId);
        if (CellValues.IsFalsy(id)) return null;
        var amounts = BudgetRules.CleanAmounts(Enumerable.Range(FirstMonthCol, 12).Select(At));
        var section = At(ColSection);
        return new BudgetLine(
            Coerce.ToText(id),
            Coerce.ToInt(At(ColYear), CurrentYear),
            CellValues.IsFalsy(section) ? "Expense" : Coerce.ToText(section).Trim(),
            CellValues.Text(At(ColGroup)).Trim(),
            CellValues.Text(At(ColItem)).Trim(),
            amounts,
            Coerce.Round(amounts.Sum()),
            CellValues.Text(At(ColNotes)));
    }

    // ------------------------------------------------------------------ payments

    public void AddAccount(Account account)
    {
        var ws = AccountsSheet;
        var row = NextRow(ws);
        ws.Cell(row, 1).Value = account.Title.Trim();
        ws.Cell(row, 2).Value = account.TotalAmount;
        SetDate(ws.Cell(row, 3), account.StartDate);
        SetDate(ws.Cell(row, 4), account.DueDate);
        SetText(ws.Cell(row, 5), account.Frequency);
        ws.Cell(row, 6).Value = account.Instalments;
        ws.Cell(row, 7).Value = account.PaymentAmount;
        SetText(ws.Cell(row, 8), account.Notes);
        FormatRow(ws, row, money: [2, 7], dates: [3, 4]);
        accounts = null;
    }

    public void AddPayment(Payment payment)
    {
        var ws = PaymentsSheet;
        var row = NextRow(ws);
        ws.Cell(row, 1).Value = payment.Id;
        ws.Cell(row, 2).Value = payment.Title.Trim();
        ws.Cell(row, 3).Value = payment.Amount;
        SetDate(ws.Cell(row, 4), payment.Date);
        SetText(ws.Cell(row, 5), payment.Time);
        ws.Cell(row, 6).Value = PaymentStatus.IsKnown(payment.Status) ? payment.Status : PaymentStatus.Unpaid;
        SetDate(ws.Cell(row, 7), payment.DueDate);
        SetText(ws.Cell(row, 8), payment.Note);
        FormatRow(ws, row, money: [3], dates: [4, 7]);
        payments = null;
    }

    public bool SetPaymentStatus(string id, string status)
    {
        if (FindRow(PaymentsSheet, 1, id) is not { } row) return false;
        PaymentsSheet.Cell(row, 6).Value = PaymentStatus.IsKnown(status) ? status : PaymentStatus.Unpaid;
        payments = null;
        return true;
    }

    public bool DeletePayment(string id)
    {
        if (FindRow(PaymentsSheet, 1, id) is not { } row) return false;
        PaymentsSheet.Row(row).Delete();
        payments = null;
        return true;
    }

    public int? DeleteAccount(string title)
    {
        var key = (title ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return null;
        if (DeleteMatchingRows(AccountsSheet, 1, key) == 0) return null;
        var removed = DeleteMatchingRows(PaymentsSheet, 2, key);
        accounts = null;
        payments = null;
        return removed;
    }

    /// <summary>Deletes every data row whose cell matches the title (case-insensitive), bottom-up.</summary>
    private static int DeleteMatchingRows(IXLWorksheet ws, int column, string titleKey)
    {
        var rows = DataRows(ws)
            .Where(r => CellValues.Read(ws.Cell(r, column)) is { } v
                && string.Equals(Coerce.ToText(v).Trim(), titleKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (var i = rows.Count - 1; i >= 0; i--) ws.Row(rows[i]).Delete();
        return rows.Count;
    }

    // ------------------------------------------------------------------ budget

    public void AddBudgetLine(BudgetLine line)
    {
        var ws = EnsureBudgetSheet();
        var row = NextRow(ws);
        ws.Cell(row, ColId).Value = line.Id;
        ws.Cell(row, ColYear).Value = line.Year;
        ws.Cell(row, ColSection).Value = line.Section;
        SetText(ws.Cell(row, ColGroup), line.Group);
        SetText(ws.Cell(row, ColItem), line.Item);
        for (var i = 0; i < 12; i++) SetMoney(ws.Cell(row, FirstMonthCol + i), line.Amounts[i]);
        SetText(ws.Cell(row, ColNotes), line.Notes);
        BudgetTouched();
    }

    public bool SetBudgetAmount(string id, int month, double value)
    {
        var ws = EnsureBudgetSheet();
        if (FindRow(ws, ColId, id) is not { } row) return false;
        SetMoney(ws.Cell(row, FirstMonthCol + month), Coerce.Round(value));
        BudgetTouched();
        return true;
    }

    public bool UpdateBudgetLine(string id, string item, string group, string? notes, double[]? amounts)
    {
        var ws = EnsureBudgetSheet();
        if (FindRow(ws, ColId, id) is not { } row) return false;
        SetText(ws.Cell(row, ColItem), item);
        SetText(ws.Cell(row, ColGroup), group);
        if (notes is not null) SetText(ws.Cell(row, ColNotes), notes);
        if (amounts is not null)
            for (var i = 0; i < 12; i++) SetMoney(ws.Cell(row, FirstMonthCol + i), amounts[i]);
        BudgetTouched();
        return true;
    }

    /// <summary>Import "replace": overwrite the months (keeping their formatting) and, if given, the notes.</summary>
    public void ReplaceBudgetAmounts(string id, double[] amounts, string? notes)
    {
        var ws = EnsureBudgetSheet();
        if (FindRow(ws, ColId, id) is not { } row) return;
        for (var i = 0; i < 12; i++) ws.Cell(row, FirstMonthCol + i).Value = amounts[i];
        if (notes is not null) SetText(ws.Cell(row, ColNotes), notes);
        BudgetTouched();
    }

    public bool DeleteBudgetLine(string id)
    {
        var ws = EnsureBudgetSheet();
        if (FindRow(ws, ColId, id) is not { } row) return false;
        ws.Row(row).Delete();
        BudgetTouched();
        return true;
    }

    public int DeleteBudgetYear(int year)
    {
        var ws = EnsureBudgetSheet();
        // Match on the parsed year so blank-year rows (shown as the current year) are included.
        var rows = DataRows(ws).Where(r => ReadLine(ws, r) is { } line && line.Year == year).ToList();
        if (rows.Count == 0) return 0;
        for (var i = rows.Count - 1; i >= 0; i--) ws.Row(rows[i]).Delete();
        BudgetTouched();
        return rows.Count;
    }

    private void BudgetTouched()
    {
        budgetLines = null;
        budgetChanged = true;
    }

    private IXLWorksheet EnsureBudgetSheet()
    {
        if (workbook.TryGetWorksheet(BudgetSheet, out var existing)) return existing;
        var ws = workbook.Worksheets.Add(BudgetSheet);
        string[] headers = ["ID", "Year", "Section", "Group", "Item", .. BudgetRules.Months, "Notes"];
        int[] widths = [38, 8, 12, 18, 30, .. Enumerable.Repeat(12, 12), 34];
        StyleHeader(ws, headers, widths, frozenColumns: 5);
        return ws;
    }

    // ------------------------------------------------------------------ save

    public void Save()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (budgetChanged) BudgetReportWriter.RebuildAll(workbook, BudgetLines());
        ClosedXmlTrackerWorkbookStore.AtomicSave(workbook, path);
        budgetChanged = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        workbook.Dispose();
        release();
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<int> DataRows(IXLWorksheet ws)
    {
        var last = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++) yield return row;
    }

    /// <summary>The first data row whose column holds exactly this id (<c>str(cell or "") == id</c>).</summary>
    private static int? FindRow(IXLWorksheet ws, int column, string id)
    {
        foreach (var row in DataRows(ws))
            if (CellValues.Text(CellValues.Read(ws.Cell(row, column))) == id) return row;
        return null;
    }

    private static int NextRow(IXLWorksheet ws) => (ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1) + 1;

    private static void SetText(IXLCell cell, string? text)
    {
        if (string.IsNullOrEmpty(text)) cell.Clear(XLClearOptions.Contents);
        else cell.Value = text;
    }

    private static void SetDate(IXLCell cell, string iso)
    {
        if (Coerce.ToDate(iso) is { } date) cell.Value = date.ToDateTime(TimeOnly.MinValue);
        else cell.Clear(XLClearOptions.Contents);
    }

    private static void SetMoney(IXLCell cell, double value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void FormatRow(IXLWorksheet ws, int row, int[] money, int[] dates)
    {
        foreach (var column in money) ws.Cell(row, column).Style.NumberFormat.Format = MoneyFormat;
        foreach (var column in dates) ws.Cell(row, column).Style.NumberFormat.Format = DateFormat;
    }
}