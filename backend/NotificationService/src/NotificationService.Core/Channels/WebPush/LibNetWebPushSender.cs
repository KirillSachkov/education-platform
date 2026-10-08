using System.Net;
using Lib.Net.Http.WebPush;
using Microsoft.Extensions.Logging;

namespace NotificationService.Core.Channels.WebPush;

/// <summary>
/// Реализация <see cref="IWebPushSender"/> поверх <c>Lib.Net.Http.WebPush</c>:
/// VAPID-аутентификация (<c>PushServiceClient.DefaultAuthentication</c>) ставится через DI
/// (<c>AddPushServiceClient</c>) + RFC 8291 payload encryption. 404/410 от push-сервиса →
/// <see cref="WebPushSendOutcome.Gone"/> (подписка протухла). Issue #342.
/// </summary>
public sealed class LibNetWebPushSender : IWebPushSender
{
    private readonly PushServiceClient _client;
    private readonly ILogger<LibNetWebPushSender> _logger;

    public LibNetWebPushSender(PushServiceClient client, ILogger<LibNetWebPushSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<WebPushSendOutcome> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        PushSubscription subscription = new()
        {
            Endpoint = endpoint,
            Keys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["p256dh"] = p256dh,
                ["auth"] = auth,
            },
        };

        PushMessage message = new(payloadJson);

        try
        {
            await _client.RequestPushMessageDeliveryAsync(subscription, message, cancellationToken);
            return WebPushSendOutcome.Delivered;
        }
        catch (PushServiceClientException ex)
            when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Браузер удалил подписку / endpoint протух — чистим, чтобы не долбить мёртвый URL.
            _logger.LogDebug(ex, "Web push endpoint gone ({StatusCode}), pruning subscription", ex.StatusCode);
            return WebPushSendOutcome.Gone;
        }
        catch (PushServiceClientException ex)
        {
            _logger.LogWarning(ex, "Web push delivery failed with {StatusCode}", ex.StatusCode);
            return WebPushSendOutcome.Failed;
        }
    }
}
