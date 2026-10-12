using Rose.Application.Reminders;

namespace Rose.Application.Abstractions;

/// <summary>Sends one Web Push message to one browser / phone.</summary>
public interface IPushSender
{
    /// <summary>The VAPID public key browsers need to subscribe.</summary>
    string PublicKey { get; }

    /// <exception cref="PushSubscriptionGoneException">The device unsubscribed (HTTP 404/410).</exception>
    Task SendAsync(PushSubscriptionInfo subscription, string payload, CancellationToken cancellationToken);
}

public sealed class PushSubscriptionGoneException(string message) : Exception(message);

/// <summary>The always-on-top popup on this Windows PC.</summary>
public interface IDesktopAlarm
{
    bool Enabled { get; }

    void Show(Domain.Reminders.Reminder reminder, string dueLabel);
}