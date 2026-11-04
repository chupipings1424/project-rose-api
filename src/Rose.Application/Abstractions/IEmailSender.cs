namespace Rose.Application.Abstractions;

/// <summary>Sends email (Brevo in the cloud). Without settings <see cref="IsConfigured"/> is false and nothing is sent.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>One email: a plain-text body for every mail app, and an HTML one for the nicer ones.</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);