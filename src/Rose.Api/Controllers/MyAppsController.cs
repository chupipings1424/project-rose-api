using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Rose.Application.Preferences;

namespace Rose.Api.Controllers;

/// <summary>"My apps": the projects you hid from your hub. Saved on the server so every device shows the same list.</summary>
[ApiController]
[Route("api/me/apps")]
[Produces("application/json")]
[Tags("My apps")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class MyAppsController(AppPrefsService prefs) : ControllerBase
{
    /// <summary>The project ids you hid. Empty = show everything.</summary>
    [HttpGet]
    public Task<AppPrefsView> Get(CancellationToken ct) => prefs.GetAsync(User.UserId(), ct);

    /// <summary>Replace the hidden list (send the whole list, e.g. <c>{"hidden":["savings"]}</c>).</summary>
    /// <response code="400">More than 50 ids, or an id that isn't lower-case letters, digits and dashes.</response>
    [HttpPut]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AppPrefsView> Set(AppPrefsRequest request, CancellationToken ct) => prefs.SetAsync(User.UserId(), request, ct);
}