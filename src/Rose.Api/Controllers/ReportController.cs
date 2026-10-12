using Microsoft.AspNetCore.Mvc;
using Rose.Application.Tracker;
using Rose.Domain.Reporting;

namespace Rose.Api.Controllers;

[Tags("Report")]
public sealed class ReportController(ReportService reports) : TrackerControllerBase
{
    /// <summary>Everything the report dashboard shows: KPIs, timeline, budget breakdown.</summary>
    [HttpGet("report/summary")]
    public Task<ReportSummary> Summary([FromQuery] string? year, CancellationToken ct) => reports.SummaryAsync(year, ct);
}