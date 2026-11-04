using System.Net;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Users;

namespace Rose.Application.Notifications;

/// <summary>The site's public address, for links in emails (null = no link).</summary>
public sealed record PublicSite(Uri? BaseUrl);

/// <summary>
/// Emails a reminder or habit nudge to its owner. A failed email is logged and never stops the
/// push notifications (or the next user's reminders).
/// </summary>
public sealed partial class EmailNotifier(IEmailSender sender, PublicSite site, ILogger<EmailNotifier> logger)
{
    public bool IsEnabled => sender.IsConfigured;

    public Task SendAsync(RoseUser user, string title, string body, string path, CancellationToken ct = default)
    {
        if (!sender.IsConfigured) return Task.CompletedTask;
        return SendCoreAsync(user, title, body, path, ct);
    }

    private async Task SendCoreAsync(RoseUser user, string title, string body, string path, CancellationToken ct)
    {
        var link = site.BaseUrl is { } baseUrl ? new Uri(baseUrl, path).ToString() : null;
        var text = link is null ? $"{title}\n{body}\n\n- ROSE" : $"{title}\n{body}\n\nOpen ROSE: {link}\n\n- ROSE";
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border-radius:12px;background:#12131c;color:#eef0ff">
              <h2 style="margin:0 0 8px;font-size:20px">{WebUtility.HtmlEncode(title)}</h2>
              <p style="margin:0 0 20px;color:#b9bdd6">{WebUtility.HtmlEncode(body)}</p>
              {(link is null ? "" : $"<a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;padding:10px 18px;border-radius:8px;background:#ff4f8b;color:#fff;text-decoration:none\">Open ROSE</a>")}
              <p style="margin:24px 0 0;font-size:12px;color:#7d81a0">Sent by ROSE because this reminder is yours.</p>
            </div>
            """;
        try
        {
            await sender.SendAsync(new EmailMessage(user.Email, title, text, html), ct);
        }
#pragma warning disable CA1031 // email is a bonus channel: report and carry on
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogEmailFailed(title, error.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email \"{Subject}\" failed: {Reason}")]
    private partial void LogEmailFailed(string subject, string reason);
}