using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Rose.Api.Auth;
using Rose.Application.Abstractions;
using Rose.Domain.Common;

namespace Rose.Api.Infrastructure;

/// <summary>
/// Makes a POST safe to retry. When the request carries an <c>Idempotency-Key</c> header
/// (any unique text, e.g. a UUID the phone made when you saved offline), the first success is
/// remembered and every repeat with the same key gets that answer back - nothing is saved twice.
/// Requests without the header behave as before.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

internal sealed class IdempotencyFilter(IIdempotencyStore store, IOptions<JsonOptions> json) : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int MaxKeyLength = 100;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString().Trim();
        if (key.Length == 0)
        {
            await next();
            return;
        }
        if (key.Length > MaxKeyLength) throw new ValidationException($"{Header} must be at most {MaxKeyLength} characters.");

        var owner = http.User.UserId();
        if (await store.FindAsync(owner, key, http.RequestAborted) is { } saved)
        {
            http.Response.Headers[ReplayedHeader] = "true";
            context.Result = new ContentResult
            {
                StatusCode = saved.StatusCode,
                Content = saved.Body,
                ContentType = "application/json; charset=utf-8",
            };
            return;
        }

        var executed = await next();
        // Only successes are remembered: after an error the phone may fix the data and try again.
        if (executed.Exception is null && executed.Result is ObjectResult { Value: { } value } result
            && (result.StatusCode ?? StatusCodes.Status200OK) is >= 200 and < 300 and var status)
        {
            var body = JsonSerializer.Serialize(value, value.GetType(), json.Value.JsonSerializerOptions);
            // Not RequestAborted: the change is saved, so its answer must be stored even if the phone hung up.
            await store.SaveAsync(owner, key, new StoredResponse(status, body), CancellationToken.None);
        }
    }
}