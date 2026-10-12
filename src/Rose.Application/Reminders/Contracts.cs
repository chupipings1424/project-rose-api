using System.Text.Json.Serialization;

namespace Rose.Application.Reminders;

/// <summary>Create or edit a reminder. <c>due_at</c> is an ISO date-time.</summary>
public sealed record ReminderRequest(string? Text, string? DueAt, string? Repeat, List<int>? WeekDays);

/// <summary>Snooze for this many minutes (1 - 1440, default 10).</summary>
public sealed record SnoozeRequest(double? Minutes);

public sealed record ReminderStatus(bool Ok, int Devices, bool NativeAlarms, DateTimeOffset Now);

/// <summary>The browser's <c>PushSubscription.toJSON()</c>.</summary>
public sealed record PushSubscriptionInfo(
    string? Endpoint,
    [property: JsonPropertyName("expirationTime")] long? ExpirationTime,
    PushKeys? Keys)
{
    [JsonIgnore]
    public bool IsValid =>
        Endpoint is not null
        && Endpoint.StartsWith("https://", StringComparison.Ordinal)
        && Keys is { P256dh: not null, Auth: not null };
}

public sealed record PushKeys(string? P256dh, string? Auth);

public sealed record PushSubscribeRequest(PushSubscriptionInfo? Subscription, string? Label);

public sealed record PushUnsubscribeRequest(string? Endpoint);

public sealed record PushKey(string PublicKey);

public sealed record PushDevices(bool Ok, int Devices);

public sealed record PushResult(int Sent, int Failed);

/// <summary>A subscribed phone or browser, as saved in push-devices.json.</summary>
public sealed record PushDevice(PushSubscriptionInfo Subscription, string Label, DateTimeOffset CreatedAt);

/// <summary>What the service worker shows. Same <see cref="Tag"/> = replaces the previous notification.</summary>
public sealed record PushMessage(string Title, string Body, string Url, string Tag);