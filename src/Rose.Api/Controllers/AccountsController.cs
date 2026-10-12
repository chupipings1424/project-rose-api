using Microsoft.AspNetCore.Mvc;
using Rose.Application.Tracker;
using Rose.Domain.Tracker;

namespace Rose.Api.Controllers;

[Tags("Titles")]
public sealed class AccountsController(PaymentTrackerService tracker) : TrackerControllerBase
{
    /// <summary>Every title (instalment plan).</summary>
    [HttpGet("accounts")]
    public Task<IReadOnlyList<Account>> List(CancellationToken ct) => tracker.AccountsAsync(ct);

    /// <summary>Create a title. With <c>instalments</c> it also schedules one Unpaid payment per instalment.</summary>
    /// <response code="409">A title with that name already exists.</response>
    [HttpPost("accounts")]
    [ProducesResponseType<IReadOnlyList<Account>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateAccountRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tracker.CreateAccountAsync(request, ct));

    /// <summary>Preview an instalment schedule without saving anything.</summary>
    [HttpPost("accounts/preview")]
    public SchedulePreview Preview(SchedulePreviewRequest request) => tracker.Preview(request);

    /// <summary>Delete a title and every payment recorded under it.</summary>
    /// <response code="404">Title not found.</response>
    [HttpDelete("accounts/{*title}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountDeleted> Delete(string title, CancellationToken ct) => tracker.DeleteAccountAsync(DecodeTitle(title), ct);

    /// <summary>Totals, progress, next due date and payments of one title.</summary>
    [HttpGet("summary/{*title}")]
    public Task<AccountSummary> Summary(string title, CancellationToken ct) => tracker.SummaryAsync(DecodeTitle(title), ct);
}