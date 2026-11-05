using Rose.Application.Abstractions;
using Rose.Domain.Reporting;

namespace Rose.Application.Tracker;

public sealed class ReportService(ITrackerWorkbookStore store, ICurrentUser user, TimeProvider clock)
{
    public async Task<ReportSummary> SummaryAsync(string? year, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(user.Id, ct);
        await PaymentBudgetSync.SyncAsync(book, Common.ClockExtensions.Today(clock), ct);
        return ReportBuilder.Build(
            book.Accounts(), book.Payments(), book.BudgetLines(), year, clock.GetLocalNow().DateTime, store.StorageName);
    }
}