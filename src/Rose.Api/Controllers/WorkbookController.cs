using Microsoft.AspNetCore.Mvc;
using Rose.Application.Abstractions;
using Rose.Application.Tracker;

namespace Rose.Api.Controllers;

[Tags("Workbook")]
public sealed class WorkbookController(PaymentTrackerService tracker, ITrackerWorkbookStore store, ICurrentUser user) : TrackerControllerBase
{
    /// <summary>Headline figures for the overview cards.</summary>
    [HttpGet("home-stats")]
    public Task<HomeStats> HomeStats(CancellationToken ct) => tracker.HomeStatsAsync(ct);

    /// <summary>The server's local date and time (pre-fills the payment form).</summary>
    [HttpGet("now")]
    public ServerNow Now() => tracker.Now();

    /// <summary>Download all your data (titles, payments, budget and yearly reports) as payments.xlsx.</summary>
    [HttpGet("download")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Download(CancellationToken ct) =>
        File(await store.ExportAsync(user.Id, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "payments.xlsx");
}