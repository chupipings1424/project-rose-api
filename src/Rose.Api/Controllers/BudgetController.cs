using Microsoft.AspNetCore.Mvc;
using Rose.Application.Tracker;
using Rose.Domain.Budget;

namespace Rose.Api.Controllers;

[Tags("Budget")]
public sealed class BudgetController(BudgetService budget) : TrackerControllerBase
{
    /// <summary>One year of the budget, grouped and totalled (defaults to this year).</summary>
    [HttpGet("budget")]
    public Task<BudgetView> Get([FromQuery] string? year, CancellationToken ct) => budget.GetAsync(year, ct);

    /// <summary>Add a budget line.</summary>
    /// <response code="409">That item already exists in the group for the year.</response>
    [HttpPost("budget/lines")]
    [ProducesResponseType<BudgetLineCreated>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLine(AddBudgetLineRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await budget.AddLineAsync(request, ct));

    /// <summary>Change one month (<c>month</c> + <c>value</c>) or the item / group / notes / amounts.</summary>
    /// <response code="404">Budget line not found.</response>
    /// <response code="409">The new name clashes with another line.</response>
    [HttpPatch("budget/lines/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BudgetChanged> UpdateLine(string id, UpdateBudgetLineRequest request, CancellationToken ct) =>
        budget.UpdateLineAsync(id, request, ct);

    /// <summary>Delete a budget line.</summary>
    /// <response code="404">Budget line not found.</response>
    [HttpDelete("budget/lines/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BudgetChanged> DeleteLine(string id, [FromQuery] string? year, CancellationToken ct) =>
        budget.DeleteLineAsync(id, year, ct);

    /// <summary>Delete every line of a year. Answers with this year's budget.</summary>
    /// <response code="404">No budget lines found for that year.</response>
    [HttpDelete("budget/years/{year:int}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BudgetYearDeleted> DeleteYear(int year, CancellationToken ct) => budget.DeleteYearAsync(year, ct);

    /// <summary>Copy a year's items (optionally with amounts) into another year.</summary>
    [HttpPost("budget/copy-year")]
    public Task<BudgetYearCopied> CopyYear(CopyBudgetYearRequest request, CancellationToken ct) => budget.CopyYearAsync(request, ct);
}