using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Rose.Api.Lifetime;

/// <summary>Whether ROSE can close itself.</summary>
public sealed record SystemInfo(bool ShutdownEnabled, double IdleTimeoutSeconds);

/// <summary>
/// Keeps ROSE alive while a page is open, and closes it on request. Anonymous on
/// purpose: the power button is also on the login page.
/// </summary>
[ApiController]
[Route("api/system")]
[AllowAnonymous]
public sealed partial class SystemController(
    PresenceTracker presence,
    IOptions<ShutdownOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<SystemController> logger) : ControllerBase
{
    /// <summary>
    /// Any custom header forces a browser to ask permission first (a CORS preflight),
    /// which this API never grants. So other websites cannot close ROSE - only ROSE's
    /// own page, which is served from the same origin.
    /// </summary>
    public const string ClientHeader = "X-Rose-Client";

    private static readonly byte[] Ping = Encoding.ASCII.GetBytes(": ping\n\n");
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);

    /// <summary>Can ROSE close itself? (Only when started by npm start / start-rose.bat.)</summary>
    [HttpGet("info")]
    public SystemInfo Info() => new(options.Value.Enabled, options.Value.IdleTimeout.TotalSeconds);

    /// <summary>
    /// A page holds this stream open while it is showing ROSE. When the last one closes,
    /// ROSE shuts down after the idle timeout. (Server-sent events: only comments.)
    /// </summary>
    [HttpGet("presence")]
    [Produces("text/event-stream")]
    public async Task Presence()
    {
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var aborted = HttpContext.RequestAborted;
        using var registration = presence.Connect();
        try
        {
            while (!aborted.IsCancellationRequested)
            {
                await Response.Body.WriteAsync(Ping, aborted);
                await Response.Body.FlushAsync(aborted);
                await Task.Delay(PingInterval, aborted);
            }
        }
        catch (OperationCanceledException)
        {
            // the page closed - that is the whole point
        }
    }

    /// <summary>Close ROSE (the API, and with it the launcher and the web UI).</summary>
    /// <response code="400">The X-Rose-Client header is missing.</response>
    /// <response code="409">ROSE was not started by its launcher, so it will not close itself.</response>
    [HttpPost("shutdown")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult Shutdown()
    {
        if (!Request.Headers.ContainsKey(ClientHeader))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"Send the {ClientHeader} header.");
        if (!options.Value.Enabled)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: "Closing is turned off - ROSE was not started with start-rose.bat or npm start.");
        }

        LogShutdown();
        // Answer first, then stop, so the page learns the request worked.
        Response.OnCompleted(() =>
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return Accepted();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown requested from the ROSE page")]
    private partial void LogShutdown();
}