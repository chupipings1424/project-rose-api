using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rose.Application.Users;

namespace Rose.Api.Auth;

[ApiController]
[Route("api")]
[Produces("application/json")]
[Tags("Auth")]
public sealed class AuthController(IOptions<SupabaseOptions> supabase, UserService users) : ControllerBase
{
    /// <summary>What the React app needs to sign in with Supabase (both values are public).</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ClientConfig Config() => new(supabase.Value.Url, supabase.Value.PublishableKey);

    /// <summary>Is this Supabase token valid and does it have a ROSE account? Returns who is signed in.</summary>
    /// <response code="401">Missing, expired or invalid token.</response>
    /// <response code="403">Valid token, but that login has no ROSE account.</response>
    [HttpGet("auth/session")]
    [AllowPendingPasswordChange]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public SessionInfo Session()
    {
        var account = AccountItem.Get(HttpContext) ?? throw new InvalidOperationException("Authorization did not resolve the account.");
        var exp = User.FindFirstValue("exp");
        return new SessionInfo(
            true,
            account.User.Email,
            long.TryParse(exp, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            account.IsAdmin,
            account.User.MustChangePassword);
    }

    /// <summary>Change your own password (required after signing in with a temporary one).</summary>
    /// <response code="400">Too short (at least 8 characters) or too long.</response>
    /// <response code="503">The server has no Supabase secret key.</response>
    [HttpPost("auth/password")]
    [AllowPendingPasswordChange]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await users.ChangeOwnPasswordAsync(User.UserId(), request, ct);
        return NoContent();
    }
}

public sealed record ClientConfig(string SupabaseUrl, string SupabaseKey);

public sealed record SessionInfo(bool Authenticated, string Email, DateTimeOffset? ExpiresAt, bool IsAdmin, bool MustChangePassword);