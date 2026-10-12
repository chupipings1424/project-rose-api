using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;

namespace Rose.Application.Reminders;

/// <summary>Remembers which devices subscribed and sends them Web Push messages.</summary>
public sealed partial class PushService
{
    public const string RemindersUrl = "/projects/reminders";

    private readonly IDataFile<List<PushDevice>> file;
    private readonly IPushSender sender;
    private readonly TimeProvider clock;
    private readonly ILogger<PushService> logger;
    private readonly Lock gate = new();
    private List<PushDevice> devices;

    public PushService(IDataFile<List<PushDevice>> file, IPushSender sender, TimeProvider clock, ILogger<PushService> logger)
    {
        this.file = file;
        this.sender = sender;
        this.clock = clock;
        this.logger = logger;
        devices = file.Read();
    }

    public string PublicKey => sender.PublicKey;

    public int Count
    {
        get { lock (gate) return devices.Count; }
    }

    public int Subscribe(PushSubscribeRequest request)
    {
        if (request.Subscription is not { IsValid: true } subscription)
            throw new ValidationException("Invalid push subscription.");
        var label = request.Label ?? "Unknown device";
        lock (gate)
        {
            devices.RemoveAll(d => d.Subscription.Endpoint == subscription.Endpoint);
            devices.Add(new PushDevice(subscription, label[..Math.Min(label.Length, 120)], clock.GetUtcNow()));
            file.Write(devices);
            return devices.Count;
        }
    }

    public int Unsubscribe(PushUnsubscribeRequest request)
    {
        if (request.Endpoint is null) throw new ValidationException("Missing endpoint.");
        lock (gate)
        {
            devices.RemoveAll(d => d.Subscription.Endpoint == request.Endpoint);
            file.Write(devices);
            return devices.Count;
        }
    }

    /// <summary>Sends to every device. Devices the push service says are gone (404/410) are forgotten.</summary>
    public async Task<PushResult> SendToAllAsync(PushMessage message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(message, RoseJson.Push);
        List<PushDevice> targets;
        lock (gate) targets = [.. devices];

        var results = await Task.WhenAll(targets.Select(async Task<Delivery> (device) =>
        {
            try
            {
                await sender.SendAsync(device.Subscription, payload, ct);
                return new Delivery(true, null);
            }
            catch (PushSubscriptionGoneException)
            {
                return new Delivery(false, device.Subscription.Endpoint);
            }
#pragma warning disable CA1031 // one bad device must not stop the others
            catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogPushFailed(device.Label, error.Message);
                return new Delivery(false, null);
            }
        }));

        var gone = results.Where(r => r.Gone is not null).Select(r => r.Gone).ToHashSet();
        if (gone.Count > 0)
        {
            lock (gate)
            {
                devices.RemoveAll(d => gone.Contains(d.Subscription.Endpoint));
                file.Write(devices);
            }
        }
        var sent = results.Count(r => r.Sent);
        return new PushResult(sent, results.Length - sent);
    }

    public async Task<PushResult> SendTestAsync(CancellationToken ct = default)
    {
        var result = await SendToAllAsync(
            new PushMessage("\U0001F339 ROSE Reminders", "Notifications work! Reminders will appear like this.", RemindersUrl, "rose-test"),
            ct);
        if (result.Sent == 0)
        {
            throw new ConflictException(Count > 0
                ? "Every device refused the test - enable notifications again."
                : "No device has notifications enabled yet.");
        }
        return result;
    }

    private readonly record struct Delivery(bool Sent, string? Gone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push to {Device} failed: {Reason}")]
    private partial void LogPushFailed(string device, string reason);
}