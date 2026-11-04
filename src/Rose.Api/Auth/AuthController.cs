using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Rose.Api.Auth;

[ApiController]
[Route("api")]
[Produces("application/json")]
[Tags("Auth")]
public sealed class AuthController(IOptions<SupabaseOptions> supabase) : ControllerBase
{
    /// <summary>What the React app needs to sign in with Supabase (both values are public).</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ClientConfig Config() => new(supabase.Value.Url, supabase.Value.PublishableKey);

    /// <summary>Is this Supabase token valid and allowed? Returns who is signed in.</summary>
    /// <response code="401">Missing, expired or invalid token.</response>
    /// <response code="403">Valid token, but that email may not use ROSE.</response>
    [HttpGet("auth/session")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public SessionInfo Session()
    {
        var exp = User.FindFirstValue("exp");
        return new SessionInfo(
            true,
            User.FindFirstValue("email") ?? "",
            long.TryParse(exp, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null);
    }
}

public sealed record ClientConfig(string SupabaseUrl, string SupabaseKey);

public sealed record SessionInfo(bool Authenticated, string Email, DateTimeOffset? ExpiresAt);