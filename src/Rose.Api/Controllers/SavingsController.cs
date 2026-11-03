using Microsoft.AspNetCore.Mvc;
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
    public IReadOnlyList<SavingsGoalView> List() => savings.List();

    /// <summary>Create a goal.</summary>
    [HttpPost("goals")]
    [ProducesResponseType<SavingsGoalView>(StatusCodes.Status201Created)]
    public IActionResult Create(GoalRequest request) =>
        StatusCode(StatusCodes.Status201Created, savings.Create(request));

    /// <summary>Rename a goal or change its target, icon or deadline.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpPatch("goals/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public SavingsGoalView Update(string id, GoalRequest request) => savings.Update(id, request);

    /// <summary>Delete a goal and its history.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpDelete("goals/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public OkResponse Delete(string id)
    {
        savings.Remove(id);
        return new OkResponse();
    }

    /// <summary>Add a deposit or a withdrawal.</summary>
    /// <response code="404">That goal no longer exists.</response>
    [HttpPost("goals/{id}/entries")]
    [ProducesResponseType<SavingsGoalView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult AddEntry(string id, EntryRequest request) =>
        StatusCode(StatusCodes.Status201Created, savings.AddEntry(id, request));

    /// <summary>Delete one deposit or withdrawal.</summary>
    /// <response code="404">That goal or entry no longer exists.</response>
    [HttpDelete("goals/{id}/entries/{entryId}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public SavingsGoalView RemoveEntry(string id, string entryId) => savings.RemoveEntry(id, entryId);
}