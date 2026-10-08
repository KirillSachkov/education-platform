using Microsoft.Extensions.Options;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.UserChannels;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>auth.events / user.telegram_linked</c> → включить Telegram-канал в настройках
/// и отправить welcome-сообщение.
///
/// Race-safe: <see cref="IUserChannelsRepository.UpsertFlagsAsync"/> выполняет
/// <c>INSERT ... ON CONFLICT DO UPDATE</c> — атомарный upsert.
/// </summary>
public sealed class UserTelegramLinkedHandler
{
    private readonly IUserChannelsRepository _userChannels;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationOptions _options;

    public UserTelegramLinkedHandler(
        IUserChannelsRepository userChannels,
        INotificationDispatcher dispatcher,
        IOptions<NotificationOptions> options)
    {
        _userChannels = userChannels;
        _dispatcher = dispatcher;
        _options = options.Value;
    }

    public async Task Handle(UserTelegramLinked evt, CancellationToken ct)
    {
        // Сохраняем существующие EmailEnabled/WebPushEnabled (если есть), включаем Telegram.
        UserNotificationChannels? existing = await _userChannels.GetByUserIdAsync(evt.UserId, ct);
        UserNotificationChannels defaults = UserNotificationChannels.Default(evt.UserId);
        bool emailEnabled = existing?.EmailEnabled ?? defaults.EmailEnabled;
        bool webPushEnabled = existing?.WebPushEnabled ?? defaults.WebPushEnabled;

        await _userChannels.UpsertFlagsAsync(
            userId: evt.UserId,
            telegramEnabled: true,
            emailEnabled: emailEnabled,
            webPushEnabled: webPushEnabled,
            cancellationToken: ct);

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.TelegramLinked,
            recipientUserId: evt.UserId,
            correlationId: evt.UserId,
            args: TemplateArgs.Of(
                ("telegramUsername", evt.TelegramUsername ?? "user"),
                ("frontendUrl", _options.FrontendBaseUrl)));

        await _dispatcher.DispatchAsync(request, ct);
    }
}
