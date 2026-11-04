using Microsoft.AspNetCore.Mvc;
using Rose.Api.Auth;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Rose.Application.Reminders;
using Rose.Application.Tracker;
using Rose.Domain.Reminders;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/reminders")]
[Produces("application/json")]
[Tags("Reminders")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class RemindersController(ReminderService reminders, PushService push, TimeProvider clock) : ControllerBase
{
    /// <summary>Is the scheduler up, and how many of your phones / browsers are subscribed?</summary>
    [HttpGet("status")]
    public async Task<ReminderStatus> Status(CancellationToken ct) => new(true, await push.CountAsync(User.UserId(), ct), clock.GetUtcNow());

    /// <summary>Every reminder, soonest first.</summary>
    [HttpGet("items")]
    public Task<IReadOnlyList<Reminder>> List(CancellationToken ct) => reminders.ListAsync(User.UserId(), ct);

    /// <summary>Create a reminder.</summary>
    [HttpPost("items")]
    [ProducesResponseType<Reminder>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ReminderRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await reminders.CreateAsync(User.UserId(), request, ct));

    /// <summary>Edit a reminder. A new time or repeat rule makes it ring again.</summary>
    /// <response code="404">That reminder no longer exists.</response>
    [HttpPatch("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Reminder> Update(string id, ReminderRequest request, CancellationToken ct) => reminders.UpdateAsync(User.UserId(), id, request, ct);

    /// <summary>Delete a reminder.</summary>
    /// <response code="404">That reminder no longer exists.</response>
    [HttpDelete("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OkResponse> Delete(string id, CancellationToken ct)
    {
        await reminders.RemoveAsync(User.UserId(), id, ct);
        return new OkResponse();
    }

    /// <summary>One-off: mark done. Repeating: silence this ring and keep the schedule.</summary>
    [HttpPost("items/{id}/complete")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Reminder> Complete(string id, CancellationToken ct) => reminders.CompleteAsync(User.UserId(), id, ct);

    /// <summary>Silence it without finishing it.</summary>
    [HttpPost("items/{id}/dismiss")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Reminder> Dismiss(string id, CancellationToken ct) => reminders.DismissAsync(User.UserId(), id, ct);

    /// <summary>Ring again in <c>minutes</c> (default 10, max 1440).</summary>
    [HttpPost("items/{id}/snooze")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Reminder> Snooze(
        string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SnoozeRequest? request, CancellationToken ct) =>
        reminders.SnoozeAsync(User.UserId(), id, request?.Minutes, ct);

    /// <summary>The VAPID public key a browser needs to subscribe.</summary>
    [HttpGet("push/key")]
    public PushKey PushKey() => new(push.PublicKey);

    /// <summary>Your phones / browsers that get notifications, oldest first.</summary>
    [HttpGet("push/devices")]
    public Task<IReadOnlyList<PushDeviceInfo>> Devices(CancellationToken ct) => push.ListAsync(User.UserId(), ct);

    /// <summary>Remember this phone / browser for your notifications (moves it over if another account had it).</summary>
    [HttpPost("push/subscribe")]
    public async Task<PushDevices> Subscribe(PushSubscribeRequest request, CancellationToken ct) =>
        new(true, await push.SubscribeAsync(User.UserId(), request, ct));

    /// <summary>Forget a phone / browser.</summary>
    [HttpPost("push/unsubscribe")]
    public async Task<PushDevices> Unsubscribe(PushUnsubscribeRequest request, CancellationToken ct) =>
        new(true, await push.UnsubscribeAsync(User.UserId(), request, ct));

    /// <summary>Send a test notification to every one of your subscribed devices.</summary>
    /// <response code="409">No device has notifications enabled (or all refused).</response>
    [HttpPost("push/test")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<PushResult> Test(CancellationToken ct) => push.SendTestAsync(User.UserId(), ct);
}