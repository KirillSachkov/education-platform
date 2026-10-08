namespace Shared.Messaging.IntegrationEvents.Auth.Events;

/// <summary>
/// Published when a user removes the Telegram link. Consumers should stop delivering
/// Telegram notifications for this user (cleanup user_links, disable channel in prefs).
/// </summary>
public sealed record UserTelegramUnlinked(Guid UserId, long TelegramUserId);
