using System.Text.Json;
using NotificationService.Core.Sse;
using NotificationService.Core.Templates;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Channels.InApp;

/// <summary>
/// Канал «на сайте» / In-app notifications channel. Запись в БД <c>notifications</c> делает
/// Dispatcher — здесь мы только пушим уже сохранённое уведомление в SSE hub.
///
/// Клик по уведомлению фронт делает по <c>NotificationDto.TargetUrl</c>; SSE нужен только
/// для realtime-инвалидации/обновления inbox.
/// </summary>
public sealed class InAppNotificationChannel : INotificationChannel
{
    private readonly ISseConnectionHub _sseHub;

    public InAppNotificationChannel(ISseConnectionHub sseHub) => _sseHub = sseHub;

    public NotificationChannel Type => NotificationChannel.InApp;

    public async Task<DeliveryResult> SendAsync(
        Notification notification,
        RenderedMessage message,
        CancellationToken cancellationToken = default)
    {
        string payload = JsonSerializer.Serialize(new SseNotificationPayload(
            Id: notification.Id.Value,
            Type: (short)notification.Type,
            TemplateId: notification.TemplateId,
            Title: message.Title,
            Body: message.Body,
            Payload: notification.Payload,
            CreatedAt: notification.CreatedAt));

        await _sseHub.PushAsync(
            notification.RecipientUserId,
            "notification.created",
            payload,
            cancellationToken);

        return DeliveryResult.Success();
    }

    private sealed record SseNotificationPayload(
        Guid Id,
        short Type,
        string TemplateId,
        string Title,
        string Body,
        string Payload,
        DateTime CreatedAt);
}
