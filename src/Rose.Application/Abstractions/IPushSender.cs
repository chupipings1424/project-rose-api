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

    void Show(DesktopAlarmRequest request);
}

/// <summary>What the popup shows and where its buttons report back to.</summary>
/// <param name="Key">One popup per key at a time (e.g. the reminder id).</param>
/// <param name="Title">The big line, e.g. the reminder text.</param>
/// <param name="Subtitle">The small line under it, e.g. "9:00 AM".</param>
/// <param name="Header">The banner at the top, e.g. "TIME'S UP".</param>
/// <param name="CallbackPath">
/// The API path the buttons POST to, plus <c>/complete</c>, <c>/snooze</c> or <c>/dismiss</c>
/// (e.g. <c>/api/reminders/items/{id}</c>).
/// </param>
public sealed record DesktopAlarmRequest(string Key, string Title, string Subtitle, string Header, string CallbackPath);