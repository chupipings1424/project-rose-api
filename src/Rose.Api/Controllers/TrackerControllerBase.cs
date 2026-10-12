using Microsoft.AspNetCore.Mvc;

namespace Rose.Api.Controllers;

/// <summary>Every Payment Tracker route lives under <c>/api/payment-tracker</c> (the path ROSE already calls).</summary>
[ApiController]
[Route("api/payment-tracker")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public abstract class TrackerControllerBase : ControllerBase
{
    /// <summary>
    /// A title with a "/" arrives as "%2F" (the server never decodes it inside a path segment),
    /// so turn it back into "/" - matching Flask's &lt;path:title&gt;.
    /// </summary>
    protected static string DecodeTitle(string title) =>
        title.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
}