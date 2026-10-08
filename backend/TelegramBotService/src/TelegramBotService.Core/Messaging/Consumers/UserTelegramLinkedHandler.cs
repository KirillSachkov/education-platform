using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;
using TelegramBotService.Core.Features.CourseChats.Services;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     На <c>user.telegram_linked</c>: пере-рассылает invite-link DM по всем активным
///     STANDARD-зачислениям юзера. Закрывает gap «записался на курс ДО привязки Telegram → invite терялся».
///     Логика общая с user-facing <c>POST /telegram/me/resync-invites</c> — в
///     <see cref="TelegramInviteResyncService"/>.
/// </summary>
public sealed class UserTelegramLinkedHandler
{
    private readonly TelegramInviteResyncService _resync;
    private readonly ILogger<UserTelegramLinkedHandler> _logger;

    public UserTelegramLinkedHandler(
        TelegramInviteResyncService resync,
        ILogger<UserTelegramLinkedHandler> logger)
    {
        _resync = resync;
        _logger = logger;
    }

    public async Task Handle(UserTelegramLinked evt, CancellationToken cancellationToken)
    {
        Result<int, Error> result =
            await _resync.ResyncInvitesAsync(evt.UserId, evt.TelegramUserId, cancellationToken);
        if (result.IsFailure)
            throw result.Error.AsTransient().ToException();

        int sent = result.Value;

        if (sent > 0)
        {
            _logger.LogInformation(
                "Re-triggered {Count} chat invites for user {UserId} after Telegram link",
                sent, evt.UserId);
        }
    }
}
