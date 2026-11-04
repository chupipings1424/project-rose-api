using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Rose.Application.Savings;
using Rose.Application.Tracker;
using Rose.Domain.Savings;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/savings")]
[Produces("application/json")]
[Tags("Savings")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class SavingsController(SavingsService savings) : ControllerBase
{
    /// <summary>Every goal with its progress, monthly amount needed and estimated finish date.</summary>
    [HttpGet("goals")]
    public Task<IReadOnlyList<SavingsGoalView>> List(CancellationToken ct) => savings.ListAsync(User.UserId(), ct);

    /// <summary>Create a goal.</summary>
    [HttpPost("goals")]
    [ProducesResponseType<SavingsGoalView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(GoalRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await savings.CreateAsync(User.UserId(), request, ct));

    /// <summary>Rename a goal or change its target, icon or deadline.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpPatch("goals/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SavingsGoalView> Update(string id, GoalRequest request, CancellationToken ct) => savings.UpdateAsync(User.UserId(), id, request, ct);

    /// <summary>Delete a goal and its history.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpDelete("goals/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OkResponse> Delete(string id, CancellationToken ct)
    {
        await savings.RemoveAsync(User.UserId(), id, ct);
        return new OkResponse();
    }

    /// <summary>Add a deposit or a withdrawal.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpPost("goals/{id}/entries")]
    [ProducesResponseType<SavingsGoalView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddEntry(string id, EntryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await savings.AddEntryAsync(User.UserId(), id, request, ct));

    /// <summary>Delete one deposit or withdrawal.</summary>
    /// <response code="404">That goal or entry no longer exists.</response>
    [HttpDelete("goals/{id}/entries/{entryId}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SavingsGoalView> RemoveEntry(string id, string entryId, CancellationToken ct) =>
        savings.RemoveEntryAsync(User.UserId(), id, entryId, ct);
}