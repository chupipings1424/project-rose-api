using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Rose.Application.Abstractions;
using Rose.Application.Reminders;
using Rose.Domain.Reminders;

namespace Rose.Api.Controllers;

[ApiController]
[Route("api/reminders")]
[Produces("application/json")]
[Tags("Reminders")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class RemindersController(
    ReminderService reminders, PushService push, IDesktopAlarm alarm, TimeProvider clock) : ControllerBase
{
    /// <summary>Is the scheduler up, how many phones are subscribed, and are desktop popups on?</summary>
    [HttpGet("status")]
    public ReminderStatus Status() => new(true, push.Count, alarm.Enabled, clock.GetUtcNow());

    /// <summary>Every reminder, soonest first.</summary>
    [HttpGet("items")]
    public IReadOnlyList<Reminder> List() => reminders.List();

    /// <summary>Create a reminder.</summary>
    [HttpPost("items")]
    [ProducesResponseType<Reminder>(StatusCodes.Status201Created)]
    public IActionResult Create(ReminderRequest request) =>
        StatusCode(StatusCodes.Status201Created, reminders.Create(request));

    /// <summary>Edit a reminder. A new time or repeat rule makes it ring again.</summary>
    /// <response code="404">That reminder no longer exists.</response>
    [HttpPatch("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Reminder Update(string id, ReminderRequest request) => reminders.Update(id, request);

    /// <summary>Delete a reminder.</summary>
    /// <response code="404">That reminder no longer exists.</response>
    [HttpDelete("items/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Application.Tracker.OkResponse Delete(string id)
    {
        reminders.Remove(id);
        return new Application.Tracker.OkResponse();
    }

    /// <summary>One-off: mark done. Repeating: silence this ring and keep the schedule.</summary>
    [HttpPost("items/{id}/complete")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Reminder Complete(string id) => reminders.Complete(id);

    /// <summary>Silence it without finishing it.</summary>
    [HttpPost("items/{id}/dismiss")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Reminder Dismiss(string id) => reminders.Dismiss(id);

    /// <summary>Ring again in <c>minutes</c> (default 10, max 1440).</summary>
    [HttpPost("items/{id}/snooze")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Reminder Snooze(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SnoozeRequest? request) =>
        reminders.Snooze(id, request?.Minutes);

    /// <summary>The VAPID public key a browser needs to subscribe.</summary>
    [HttpGet("push/key")]
    public PushKey PushKey() => new(push.PublicKey);

    /// <summary>Remember this phone / browser for notifications.</summary>
    [HttpPost("push/subscribe")]
    public PushDevices Subscribe(PushSubscribeRequest request) => new(true, push.Subscribe(request));

    /// <summary>Forget a phone / browser.</summary>
    [HttpPost("push/unsubscribe")]
    public PushDevices Unsubscribe(PushUnsubscribeRequest request) => new(true, push.Unsubscribe(request));

    /// <summary>Send a test notification to every subscribed device.</summary>
    /// <response code="409">No device has notifications enabled (or all refused).</response>
    [HttpPost("push/test")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<PushResult> Test(CancellationToken ct) => push.SendTestAsync(ct);
}