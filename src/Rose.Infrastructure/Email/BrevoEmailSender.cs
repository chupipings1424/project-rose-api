using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;

namespace Rose.Infrastructure.Email;

/// <summary>The "Email" section. Leave the key empty to send no email at all.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>A Brevo API key (brevo.com, free: 300 emails a day). Server only.</summary>
    public string? BrevoApiKey { get; set; }

    /// <summary>The sender address; it must be a verified sender in Brevo.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "ROSE";

    /// <summary>The site's address for "Open ROSE" links. On Render, RENDER_EXTERNAL_URL is used when this is empty.</summary>
    public string? AppUrl { get; set; }
}

/// <summary>
/// Sends email through Brevo's HTTPS API. (Render's free plan blocks SMTP ports, so plain SMTP
/// would not work there.)
/// </summary>
internal sealed class BrevoEmailSender(IHttpClientFactory http, IOptions<EmailOptions> options) : IEmailSender
{
    public const string ClientName = "brevo";

    private static readonly Uri Endpoint = new("https://api.brevo.com/v3/smtp/email");

    private readonly EmailOptions settings = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.BrevoApiKey) && !string.IsNullOrWhiteSpace(settings.FromAddress);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = settings.FromName, email = settings.FromAddress },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                textContent = message.Text,
                htmlContent = message.Html,
            }),
        };
        request.Headers.Add("api-key", settings.BrevoApiKey);
        using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Brevo answered {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}", null, response.StatusCode);
    }
}