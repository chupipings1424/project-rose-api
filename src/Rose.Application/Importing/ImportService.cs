using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Application.Importing;

/// <summary>
/// Bulk import of history. A dry run only returns the plan; applying writes every accepted
/// row and saves the workbook once (re-saving per row would be far too slow for a year of data).
/// </summary>
public sealed class ImportService(ITrackerWorkbookStore store, IUploadReader reader, TimeProvider clock)
{
    public async Task<ImportPlan> BudgetAsync(
        string fileName, byte[] content, string? mode, string? year, bool dryRun, CancellationToken ct = default)
    {
        var sheets = reader.Read(fileName, content);
        using var book = await store.OpenAsync(ct);
        var plan = ImportPlanner.AnalyzeBudget(sheets, year, mode, book.BudgetLines(), clock.Today().Year);
        if (dryRun) return plan;

        var additions = plan.Entries.Where(e => e.Action == ImportAction.Add).ToList();
        var replacements = plan.Entries.Where(e => e.Action == ImportAction.Replace).ToList();
        if (additions.Count == 0 && replacements.Count == 0) return plan;

        foreach (var entry in replacements)
            book.ReplaceBudgetAmounts(entry.ExistingId!, entry.Amounts!, string.IsNullOrEmpty(entry.Notes) ? null : entry.Notes);
        foreach (var entry in additions)
        {
            book.AddBudgetLine(new BudgetLine(
                Guid.NewGuid().ToString(), entry.Year!.Value, entry.Section!, entry.Group!, entry.Item,
                entry.Amounts!, entry.Total!.Value, entry.Notes ?? ""));
        }
        book.Save();

        var years = additions.Concat(replacements).Select(e => e.Year!.Value).Distinct().Order().ToList();
        return plan with { Applied = true, Years = years };
    }

    public async Task<ImportPlan> PaymentsAsync(
        string fileName, byte[] content, string? mode, bool dryRun, CancellationToken ct = default)
    {
        var sheets = reader.Read(fileName, content);
        using var book = await store.OpenAsync(ct);
        var plan = ImportPlanner.AnalyzePayments(sheets, mode, book.Accounts(), book.Payments());
        if (dryRun) return plan;

        var accounts = plan.Entries.Where(e => e.Kind == "account" && e.Action == ImportAction.Add).ToList();
        var payments = plan.Entries.Where(e => e.Kind == "payment" && e.Action == ImportAction.Add).ToList();
        if (accounts.Count == 0 && payments.Count == 0) return plan;

        foreach (var entry in accounts)
        {
            book.AddAccount(new Account(
                entry.Item, entry.TotalAmount ?? 0, entry.StartDate ?? "", entry.DueDate ?? "",
                entry.Frequency ?? "Monthly", entry.Instalments ?? 0, entry.PaymentAmount ?? 0, entry.Notes ?? ""));
        }
        foreach (var entry in payments)
        {
            book.AddPayment(new Payment(
                Guid.NewGuid().ToString(), entry.Item, entry.Amount!.Value, entry.Date!, entry.Time!,
                entry.Status!, Coerce.Iso(entry.DueDate), entry.Note ?? ""));
        }
        book.Save();
        return plan with { Applied = true };
    }
}