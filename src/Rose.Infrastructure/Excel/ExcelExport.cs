using Rose.Application.Abstractions;

namespace Rose.Infrastructure.Excel;

/// <summary>Writes the tracker data into a payments.xlsx in the classic layout (plus the yearly budget reports).</summary>
internal static class ExcelExport
{
    public static byte[] Build(ITrackerWorkbook source, TimeProvider clock)
    {
        using var excel = ClosedXmlTrackerWorkbook.CreateEmpty(clock);
        foreach (var account in source.Accounts()) excel.AddAccount(account);
        foreach (var payment in source.Payments()) excel.AddPayment(payment);
        foreach (var line in source.BudgetLines()) excel.AddBudgetLine(line);
        return excel.ToBytes();
    }
}