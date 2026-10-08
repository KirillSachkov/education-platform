using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Domain.UserChannels;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>auth.events / user.telegram_unlinked</c> → выключаем Telegram-канал у пользователя.
/// Side-effect only — уведомления об отвязке не создаём (по UX).
///
/// Race-safe: атомарный upsert (см. <see cref="IUserChannelsRepository.UpsertFlagsAsync"/>).
/// </summary>
public sealed class UserTelegramUnlinkedHandler
{
    private readonly IUserChannelsRepository _userChannels;
    private readonly ILogger<UserTelegramUnlinkedHandler> _logger;

    public UserTelegramUnlinkedHandler(
        IUserChannelsRepository userChannels,
        ILogger<UserTelegramUnlinkedHandler> logger)
    {
        _userChannels = userChannels;
        _logger = logger;
    }

    public async Task Handle(UserTelegramUnlinked evt, CancellationToken ct)
    {
        UserNotificationChannels? existing = await _userChannels.GetByUserIdAsync(evt.UserId, ct);
        UserNotificationChannels defaults = UserNotificationChannels.Default(evt.UserId);
        bool emailEnabled = existing?.EmailEnabled ?? defaults.EmailEnabled;
        bool webPushEnabled = existing?.WebPushEnabled ?? defaults.WebPushEnabled;

        await _userChannels.UpsertFlagsAsync(
            userId: evt.UserId,
            telegramEnabled: false,
            emailEnabled: emailEnabled,
            webPushEnabled: webPushEnabled,
            cancellationToken: ct);

        _logger.LogDebug("Disabled Telegram channel for user {UserId}", evt.UserId);
    }
}
