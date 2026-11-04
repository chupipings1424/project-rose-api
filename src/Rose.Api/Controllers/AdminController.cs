using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Rose.Application.Users;

namespace Rose.Api.Controllers;

/// <summary>Accounts: only admins (Access:AdminEmails) may list, add or remove them.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = SupabaseAuth.AdminPolicy)]
[Produces("application/json")]
[Tags("Admin")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class AdminController(UserService users) : ControllerBase
{
    /// <summary>Every account, admins first.</summary>
    [HttpGet]
    public Task<IReadOnlyList<UserView>> List(CancellationToken ct) => users.ListAsync(ct);

    /// <summary>Add an account with a temporary password; they must change it when they first sign in.</summary>
    /// <response code="400">Invalid email or password.</response>
    /// <response code="409">That email already has an account or login.</response>
    [HttpPost]
    [ProducesResponseType<UserView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateAsync(request, ct));

    /// <summary>Give an account a new temporary password (they must change it again).</summary>
    /// <response code="404">That account no longer exists.</response>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserView> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct) =>
        users.ResetPasswordAsync(id, request, ct);

    /// <summary>Remove an account: its login and every reminder, payment, goal and habit it owns.</summary>
    /// <response code="404">That account no longer exists.</response>
    /// <response code="409">You can't remove yourself or another admin.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await users.RemoveAsync(User.UserId(), id, ct);
        return NoContent();
    }
}