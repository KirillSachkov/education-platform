using NotificationService.Core.Notifications;

namespace NotificationService.Core.Dispatching;

/// <summary>
/// Принимает <see cref="NotificationRequest"/>, применяет user-channels, рендерит per-channel,
/// создаёт <see cref="Domain.Notifications.Notification"/> (идемпотентно по correlation_id),
/// публикует <c>NotificationCreated</c> в outbox и отправляет в зарегистрированные каналы.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken ct = default);

    /// <summary>
    /// Удобная обёртка для single-request диспатча.
    /// </summary>
    Task DispatchAsync(NotificationRequest request, CancellationToken ct = default) =>
        DispatchAsync([request], ct);
}
