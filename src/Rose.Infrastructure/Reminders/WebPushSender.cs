using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Application.Reminders;

namespace Rose.Infrastructure.Reminders;

/// <summary>Sends encrypted Web Push messages (RFC 8030 / 8291) signed with our VAPID keys.</summary>
public sealed class WebPushSender(IHttpClientFactory httpClients, VapidKeys keys, IOptions<ReminderOptions> options)
    : IPushSender, IDisposable
{
    // If the phone is offline, the push service keeps trying for 6 hours.
    private const int TimeToLiveSeconds = 6 * 60 * 60;

    private readonly VapidAuthentication authentication = new(keys.PublicKey, keys.PrivateKey)
    {
        Subject = options.Value.PushContact,
    };

    public string PublicKey => keys.PublicKey;

    public void Dispose() => authentication.Dispose();

    public async Task SendAsync(PushSubscriptionInfo subscription, string payload, CancellationToken cancellationToken)
    {
        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.Keys!.P256dh!);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Keys!.Auth!);

        var client = new PushServiceClient(httpClients.CreateClient(nameof(WebPushSender)))
        {
            DefaultAuthentication = authentication,
            DefaultAuthenticationScheme = VapidAuthenticationScheme.Vapid,
        };
        var message = new Lib.Net.Http.WebPush.PushMessage(payload)
        {
            TimeToLive = TimeToLiveSeconds,
            Urgency = PushMessageUrgency.High,
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target, message, cancellationToken);
        }
        catch (PushServiceClientException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new PushSubscriptionGoneException(error.Message);
        }
    }
}