namespace Shared.Messaging.IntegrationEvents.Notifications.Events;

/// <summary>
/// Published when a user marks a notification as read.
/// </summary>
public sealed record NotificationRead(
    Guid NotificationId,
    Guid UserId,
    DateTimeOffset ReadAt);
