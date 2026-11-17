using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Rose.Application.Health;
using Rose.Domain.Health;

namespace Rose.Api.Controllers;

/// <summary>
/// The Health Tracker: meals (calories typed in), finished fasts, walks and weight, with estimates.
/// Admins only, for now. Every change returns the fresh overview of the last 30 days.
/// </summary>
[ApiController]
[Route("api/health")]
[Authorize(Policy = SupabaseAuth.AdminPolicy)]
[Produces("application/json")]
[Tags("Health")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class HealthController(HealthService health) : ControllerBase
{
    /// <summary>Profile, today, this week, the fasting streak and the last <c>days</c> days (default 30, max 366), newest first.</summary>
    [HttpGet]
    public Task<HealthOverview> Overview([FromQuery] int? days, CancellationToken ct) => health.OverviewAsync(User.UserId(), days, ct);

    /// <summary>Save height, birth year, sex and goal weight (used for calories burned and BMI).</summary>
    [HttpPut("profile")]
    public Task<HealthOverview> Profile(HealthProfileRequest request, CancellationToken ct) => health.SaveProfileAsync(User.UserId(), request, ct);

    /// <summary>Set a day's weight, walk and finished fast (<c>date</c> is yyyy-MM-dd, today or earlier).</summary>
    [HttpPut("days/{date}")]
    public Task<HealthOverview> LogDay(string date, HealthDayRequest request, CancellationToken ct) =>
        health.LogDayAsync(User.UserId(), date, request, ct);

    /// <summary>Add something you ate that day.</summary>
    [HttpPost("days/{date}/meals")]
    public Task<HealthOverview> AddMeal(string date, HealthMealRequest request, CancellationToken ct) =>
        health.AddMealAsync(User.UserId(), date, request, ct);

    /// <summary>Remove a meal.</summary>
    /// <response code="404">That meal is already gone.</response>
    [HttpDelete("days/{date}/meals/{mealId}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HealthOverview> RemoveMeal(string date, string mealId, CancellationToken ct) =>
        health.RemoveMealAsync(User.UserId(), date, mealId, ct);
}