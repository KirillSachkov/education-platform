using System.Text.Json;
using NotificationService.Core.Diagnostics;
using NotificationService.Core.Sse;
using NotificationService.Domain.Notifications;
using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace NotificationService.Core.Messaging;

/// <summary>
/// Wolverine consumer на <c>notification.created</c>. Пушит событие в SSE-канал юзера.
///
/// <para>
/// Dispatch path зависит от <see cref="ISseRedisPublisher.IsEnabled"/>:
/// </para>
/// <list type="bullet">
///   <item><b>Single-replica</b> (Redis fanout disabled): push сразу в локальный hub.
///     Юзер может быть только на этой реплике — больше некому ловить.</item>
///   <item><b>Multi-replica</b> (Redis fanout enabled): publish в Redis канал
///     <c>notifications:sse:user:{userId}</c>. <c>SseRedisSubscriberService</c> на КАЖДОЙ реплике
///     (включая эту) ловит событие и пушит в свой hub — реплика с SSE-connection юзера доставит.
///     Локально тут НЕ push'им — иначе дубль (Redis тоже вернёт на эту реплику).</item>
/// </list>
/// </summary>
public sealed class SseFanoutHandler
{
    public async Task Handle(
        NotificationCreated evt,
        ISseConnectionHub hub,
        ISseRedisPublisher redisPublisher,
        NotificationMetrics metrics,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(redisPublisher);
        ArgumentNullException.ThrowIfNull(metrics);

        // Outbox publish lag = from event creation in NotificationDispatcher to consumer ack.
        // Spike here ⇒ outgoing envelope stuck on publisher side (issue #20 регрессия).
        metrics.RecordOutboxLag(
            consumer: "sse_fanout",
            type: (NotificationType)evt.Type,
            lag: DateTimeOffset.UtcNow - evt.CreatedAt);

        string json = JsonSerializer.Serialize(new
        {
            id = evt.NotificationId,
            type = evt.Type,
            templateId = evt.TemplateId,
            title = evt.Title,
            body = evt.Body,
            payload = evt.PayloadJson,
            createdAt = evt.CreatedAt,
        });

        if (redisPublisher.IsEnabled)
        {
            await redisPublisher.PublishAsync(evt.RecipientUserId, "notification.created", json, ct);
        }
        else
        {
            await hub.PushAsync(evt.RecipientUserId, "notification.created", json, ct);
        }
    }
}
