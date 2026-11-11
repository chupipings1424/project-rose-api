using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Rose.Application.Loans;
using Rose.Application.Tracker;
using Rose.Domain.Loans;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/loans")]
[Produces("application/json")]
[Tags("Loans")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class LoansController(LoanService loans) : ControllerBase
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Every loan with what is paid today, the balance, the next due date, the finish date and the schedule.</summary>
    [HttpGet]
    public Task<IReadOnlyList<LoanView>> List(CancellationToken ct) => loans.ListAsync(User.UserId(), ct);

    /// <summary>The loan types the form offers.</summary>
    [HttpGet("types")]
    public IReadOnlyList<string> Types() => LoanRules.Types;

    /// <summary>Add a loan. Payments fall on the loan date's day of every following month.</summary>
    [HttpPost]
    [ProducesResponseType<LoanView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(LoanRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await loans.CreateAsync(User.UserId(), request, ct));

    /// <summary>Change a loan.</summary>
    /// <response code="404">That loan no longer exists.</response>
    [HttpPatch("{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<LoanView> Update(string id, LoanRequest request, CancellationToken ct) => loans.UpdateAsync(User.UserId(), id, request, ct);

    /// <summary>Delete a loan.</summary>
    /// <response code="404">That loan no longer exists.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OkResponse> Delete(string id, CancellationToken ct)
    {
        await loans.RemoveAsync(User.UserId(), id, ct);
        return new OkResponse();
    }

    /// <summary>Download the loan report as loans.xlsx (a summary sheet plus each loan's schedule).</summary>
    [HttpGet("report")]
    [Produces(ExcelType)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Report(CancellationToken ct) =>
        File(await loans.ReportAsync(User.UserId(), ct), ExcelType, "loans.xlsx");
}