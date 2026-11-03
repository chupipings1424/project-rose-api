using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Rose.Application.Habits;
using Rose.Application.Reminders;
using Rose.Application.Tracker;
using Rose.Domain.Habits;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/habits")]
[Produces("application/json")]
[Tags("Habits")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class HabitsController(HabitService habits) : ControllerBase
{
    /// <summary>Every habit with today's progress, streaks and the last 13 weeks of log.</summary>
    [HttpGet("items")]
    public IReadOnlyList<HabitView> List() => habits.List();

    /// <summary>Create a habit.</summary>
    [HttpPost("items")]
    [ProducesResponseType<HabitView>(StatusCodes.Status201Created)]
    public IActionResult Create(HabitRequest request) =>
        StatusCode(StatusCodes.Status201Created, habits.Create(request));

    /// <summary>Edit a habit. Switching count -> check keeps only completed days.</summary>
    /// <response code="404">That habit no longer exists.</response>
    [HttpPatch("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public HabitView Update(string id, HabitRequest request) => habits.Update(id, request);

    /// <summary>Delete a habit and its history.</summary>
    /// <response code="404">That habit no longer exists.</response>
    [HttpDelete("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public OkResponse Delete(string id)
    {
        habits.Remove(id);
        return new OkResponse();
    }

    /// <summary>Set how much was done on <c>date</c> (yyyy-MM-dd, today or the 6 days before). 0 clears it.</summary>
    /// <response code="404">That habit no longer exists.</response>
    [HttpPut("items/{id}/days/{date}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public HabitView SetDay(string id, string date, DayRequest request) => habits.SetDay(id, date, request.Value);

    /// <summary>Nudge popup "Mark as done": today is complete.</summary>
    [HttpPost("items/{id}/nudge/complete")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public HabitView CompleteNudge(string id) => habits.CompleteToday(id);

    /// <summary>Nudge popup "Dismiss": no more nudges today.</summary>
    [HttpPost("items/{id}/nudge/dismiss")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public HabitView DismissNudge(string id) => habits.DismissNudge(id);

    /// <summary>Nudge again in <c>minutes</c> (default 10, max 1440).</summary>
    [HttpPost("items/{id}/nudge/snooze")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public HabitView SnoozeNudge(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SnoozeRequest? request) =>
        habits.SnoozeNudge(id, request?.Minutes);
}