using Microsoft.AspNetCore.Mvc;
using Rose.Application.Today;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/today")]
[Produces("application/json")]
[Tags("Today")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class TodayController(TodayService today) : ControllerBase
{
    /// <summary>The Today page: unpaid payments due within 7 days (or overdue), today's reminders, habits and this month's budget.</summary>
    [HttpGet]
    public Task<TodayView> Get(CancellationToken ct) => today.GetAsync(ct);
}