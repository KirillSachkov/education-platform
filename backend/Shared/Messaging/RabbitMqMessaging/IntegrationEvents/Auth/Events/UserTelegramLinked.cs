namespace Shared.Messaging.IntegrationEvents.Auth.Events;

/// <summary>
/// Published when a user successfully links their Telegram account to the platform.
/// NotificationService consumes this to auto-enable the Telegram channel in user preferences
/// for the notification types that support it by default.
/// </summary>
/// <param name="TelegramUserId">Telegram numeric user id (same as chat id for private chats).</param>
/// <param name="TelegramUsername">Optional @username — used for display only.</param>
public sealed record UserTelegramLinked(
    Guid UserId,
    long TelegramUserId,
    string? TelegramUsername);
