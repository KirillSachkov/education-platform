using Microsoft.Extensions.Options;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>auth.events / user.created</c> → welcome-уведомление + дефолтная запись <c>UserNotificationChannels</c>.
///
/// Событие публикуется AuthService после подтверждения email (или сразу для OAuth/Admin flow) —
/// это и есть сигнал «регистрация завершена». Отдельного <c>UserEmailConfirmed</c> нет.
///
/// Welcome форсится в InApp + Email. Email включён в дефолтных настройках, так что письмо
/// доходит из коробки; пользователь может выключить в профиле после.
///
/// Race-safe: <see cref="IUserChannelsRepository.EnsureDefaultAsync"/> делает
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> — concurrent retry'и через Wolverine inbox
/// не плодят дубликатов и не падают на unique violation.
/// </summary>
public sealed class UserCreatedHandler
{
    private readonly IUserChannelsRepository _userChannels;
    private readonly INotificationDispatcher _dispatcher;
    private readonly NotificationOptions _options;

    public UserCreatedHandler(
        IUserChannelsRepository userChannels,
        INotificationDispatcher dispatcher,
        IOptions<NotificationOptions> options)
    {
        _userChannels = userChannels;
        _dispatcher = dispatcher;
        _options = options.Value;
    }

    public async Task Handle(UserCreated evt, CancellationToken ct)
    {
        await _userChannels.EnsureDefaultAsync(evt.UserId, ct);

        string displayName = evt.DisplayName ?? evt.Username ?? "друг";

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.Welcome,
            recipientUserId: evt.UserId,
            correlationId: evt.UserId,
            args: TemplateArgs.Of(
                ("displayName", displayName),
                ("frontendUrl", _options.FrontendBaseUrl)));

        await _dispatcher.DispatchAsync(request, ct);
    }
}
