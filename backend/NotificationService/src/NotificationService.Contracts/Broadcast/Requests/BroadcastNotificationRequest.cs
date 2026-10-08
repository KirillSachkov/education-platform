namespace NotificationService.Contracts.Broadcast.Requests;

/// <summary>
/// Ручная рассылка уведомления подписчикам сущности / Manual broadcast to entity subscribers.
/// </summary>
/// <param name="TargetType">"course" | "author" | "module". В Phase 2B поддерживается только "course".</param>
/// <param name="Channels">
/// Bitmask желаемых каналов (1=InApp, 2=Telegram, 4=Email). Если <c>null</c> — берётся
/// системный дефолт для типа <c>AuthorAnnouncement</c>.
/// </param>
public sealed record BroadcastNotificationRequest(
    string TargetType,
    Guid TargetId,
    string Title,
    string Body,
    short? Channels = null);

public sealed record BroadcastNotificationResponse(Guid BroadcastId, int EstimatedRecipients);
