using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Rose.Api.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(TokenService tokens) : ControllerBase
{
    public const string LoginRateLimit = "login";

    /// <summary>Exchange the access token for a session token.</summary>
    /// <response code="401">The access token is wrong.</response>
    /// <response code="429">Too many attempts; wait a minute.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public ActionResult<LoginResponse> Login(LoginRequest request)
    {
        if (!tokens.IsValidAccessToken(request.Token))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Access denied - that token is not valid.");
        return tokens.CreateSession();
    }

    /// <summary>Is this session token still valid?</summary>
    [HttpGet("session")]
    public SessionInfo Session()
    {
        var exp = User.FindFirstValue("exp");
        return new SessionInfo(true, long.TryParse(exp, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null);
    }
}