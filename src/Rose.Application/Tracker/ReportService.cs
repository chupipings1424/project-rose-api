using Rose.Application.Abstractions;
using Rose.Domain.Reporting;

namespace Rose.Application.Tracker;

public sealed class ReportService(ITrackerWorkbookStore store, TimeProvider clock)
{
    public async Task<ReportSummary> SummaryAsync(string? year, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        return ReportBuilder.Build(
            book.Accounts(), book.Payments(), book.BudgetLines(), year, clock.GetLocalNow().DateTime, store.WorkbookPath);
    }
}