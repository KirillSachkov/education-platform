using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Database;
using NotificationService.Core.Templates;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.WebPush;

namespace NotificationService.Core.Channels.WebPush;

/// <summary>
/// Канал доставки Web Push / Web Push delivery channel. Issue #342.
///
/// Переиспользует InApp-вариант (<see cref="RenderedMessage.Title"/>/<see cref="RenderedMessage.Body"/>) —
/// отдельного template-part у WebPush нет. Отправляет VAPID-подписанный payload на каждый
/// зарегистрированный endpoint пользователя; протухшие (404/410) удаляет. Доставка
/// синхронная (как Email), без нового RabbitMQ-события.
/// </summary>
public sealed class WebPushNotificationChannel : INotificationChannel
{
    private readonly IWebPushSubscriptionsRepository _subscriptions;
    private readonly IWebPushSender _sender;
    private readonly NotificationOptions _options;
    private readonly ILogger<WebPushNotificationChannel> _logger;

    public WebPushNotificationChannel(
        IWebPushSubscriptionsRepository subscriptions,
        IWebPushSender sender,
        IOptions<NotificationOptions> options,
        ILogger<WebPushNotificationChannel> logger)
    {
        _subscriptions = subscriptions;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public NotificationChannel Type => NotificationChannel.WebPush;

    public async Task<DeliveryResult> SendAsync(
        Notification notification,
        RenderedMessage message,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WebPushSubscription> subs =
            await _subscriptions.GetByUserIdAsync(notification.RecipientUserId, cancellationToken);

        if (subs.Count == 0)
            return DeliveryResult.Skipped("web_push.no_subscription");

        string payloadJson = BuildPayloadJson(notification, message);

        int delivered = 0;
        int pruned = 0;
        int failed = 0;

        foreach (WebPushSubscription sub in subs)
        {
            WebPushSendOutcome outcome = await _sender.SendAsync(
                sub.Endpoint, sub.P256dh, sub.Auth, payloadJson, cancellationToken);

            switch (outcome)
            {
                case WebPushSendOutcome.Delivered:
                    delivered++;
                    break;
                case WebPushSendOutcome.Gone:
                    await _subscriptions.PruneEndpointAsync(sub.Endpoint, cancellationToken);
                    pruned++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (delivered > 0)
            return DeliveryResult.Success($"delivered:{delivered}");

        // Все endpoint'ы протухли → подписок фактически нет: трактуем как skip, не как ошибку.
        if (pruned > 0 && failed == 0)
            return DeliveryResult.Skipped("web_push.all_expired");

        _logger.LogWarning(
            "Web push delivery produced no successes for {NotificationId} (failed={Failed}, pruned={Pruned})",
            notification.Id.Value, failed, pruned);
        return DeliveryResult.Failed("web_push.delivery_failed", $"failed:{failed},pruned:{pruned}");
    }

    /// <summary>
    /// {title, body, url, tag} — формат, который ждёт <c>push</c>-handler в service worker'е.
    /// <c>url</c> берём из <c>payload.targetUrl</c> (запекается диспатчером), fallback —
    /// <see cref="NotificationOptions.FrontendBaseUrl"/>. <c>tag</c> = id уведомления, чтобы
    /// браузер схлопывал дубликаты.
    /// </summary>
    private string BuildPayloadJson(Notification notification, RenderedMessage message)
    {
        string url = ExtractTargetUrl(notification.Payload) ?? _options.FrontendBaseUrl;

        var payload = new
        {
            title = message.Title,
            body = message.Body,
            url,
            tag = notification.Id.Value.ToString(),
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string? ExtractTargetUrl(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("targetUrl", out JsonElement targetUrl)
                && targetUrl.ValueKind == JsonValueKind.String)
            {
                return targetUrl.GetString();
            }
        }
        catch (JsonException)
        {
            // payload не JSON — fallback на FrontendBaseUrl
        }

        return null;
    }
}
