namespace Shared.Messaging.IntegrationEvents.Notifications.Events;

/// <summary>
/// Published when an author requests a broadcast notification to subscribers
/// of a specific target (course / author / module). A background fan-out handler
/// materialises it into N NotificationCreated records.
/// </summary>
/// <param name="TargetType">"course" | "author" | "module".</param>
/// <param name="Channels">Bitmask of requested channels (InApp=1, Telegram=2, Email=4). 0 = use template default.</param>
public sealed record NotificationBroadcastRequested(
    Guid BroadcastId,
    Guid RequestedByUserId,
    string TargetType,
    Guid TargetId,
    string TemplateId,
    string Title,
    string Body,
    short Channels,
    string PayloadJson,
    DateTimeOffset RequestedAt);
