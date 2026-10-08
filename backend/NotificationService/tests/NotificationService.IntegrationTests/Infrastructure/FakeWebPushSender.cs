using System.Collections.Concurrent;
using NotificationService.Core.Channels.WebPush;

namespace NotificationService.IntegrationTests.Infrastructure;

/// <summary>
/// Test double для <see cref="IWebPushSender"/> — записывает попытки доставки вместо
/// реального VAPID-вызова к push-сервису. Позволяет ассертить, что WebPush-канал
/// был задействован, без сети (issue #342).
/// </summary>
public sealed class FakeWebPushSender : IWebPushSender
{
    public ConcurrentBag<SentPush> Sent { get; } = [];

    /// <summary>Управляемый исход — по умолчанию Delivered. Тест может выставить Gone для prune-сценария.</summary>
    public WebPushSendOutcome Outcome { get; set; } = WebPushSendOutcome.Delivered;

    public Task<WebPushSendOutcome> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        Sent.Add(new SentPush(endpoint, p256dh, auth, payloadJson));
        return Task.FromResult(Outcome);
    }

    public void Clear()
    {
        Sent.Clear();
        Outcome = WebPushSendOutcome.Delivered;
    }

    public sealed record SentPush(string Endpoint, string P256dh, string Auth, string PayloadJson);
}
