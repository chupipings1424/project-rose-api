using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;

namespace Rose.Application.Reminders;

/// <summary>Remembers which devices subscribed and sends them Web Push messages.</summary>
public sealed partial class PushService(
    IRepository<PushDevice> repository, IPushSender sender, TimeProvider clock, ILogger<PushService> logger) : IDisposable
{
    public const string RemindersUrl = "/projects/reminders";

    private readonly RepositoryCache<PushDevice> cache = new(repository);

    public string PublicKey => sender.PublicKey;

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(ct);
        return session.Items.Count;
    }

    public async Task<int> SubscribeAsync(PushSubscribeRequest request, CancellationToken ct = default)
    {
        if (request.Subscription is not { IsValid: true } subscription)
            throw new ValidationException("Invalid push subscription.");
        var label = request.Label ?? "Unknown device";
        var device = new PushDevice(subscription, label[..Math.Min(label.Length, 120)], clock.GetUtcNow());
        using var session = await cache.OpenAsync(ct);
        await session.SaveAsync(device, ct);
        session.Items.RemoveAll(d => d.Subscription.Endpoint == subscription.Endpoint);
        session.Items.Add(device);
        return session.Items.Count;
    }

    public async Task<int> UnsubscribeAsync(PushUnsubscribeRequest request, CancellationToken ct = default)
    {
        if (request.Endpoint is null) throw new ValidationException("Missing endpoint.");
        using var session = await cache.OpenAsync(ct);
        await session.DeleteAsync(request.Endpoint, ct);
        session.Items.RemoveAll(d => d.Subscription.Endpoint == request.Endpoint);
        return session.Items.Count;
    }

    /// <summary>Sends to every device. Devices the push service says are gone (404/410) are forgotten.</summary>
    public async Task<PushResult> SendToAllAsync(PushMessage message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(message, RoseJson.Push);
        List<PushDevice> targets;
        using (var session = await cache.OpenAsync(ct)) targets = [.. session.Items];

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

        var gone = results.Where(r => r.Gone is not null).Select(r => r.Gone!).ToHashSet();
        if (gone.Count > 0)
        {
            using var session = await cache.OpenAsync(ct);
            foreach (var endpoint in gone) await session.DeleteAsync(endpoint, ct);
            session.Items.RemoveAll(d => gone.Contains(d.Subscription.Endpoint!));
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
            throw new ConflictException(await CountAsync(ct) > 0
                ? "Every device refused the test - enable notifications again."
                : "No device has notifications enabled yet.");
        }
        return result;
    }

    public void Dispose() => cache.Dispose();

    private readonly record struct Delivery(bool Sent, string? Gone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push to {Device} failed: {Reason}")]
    private partial void LogPushFailed(string device, string reason);
}