using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using TelegramBotService.Core.Database;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     Wolverine handler — на <c>user.telegram_unlinked</c> чистит локальный <c>UserLink</c>,
///     когда пользователь отвязал Telegram через сайт. Cleanup handler for <c>user.telegram_unlinked</c>.
/// </summary>
public sealed class UserTelegramUnlinkedCleanupHandler
{
    private readonly IUserLinkRepository _userLinks;
    private readonly ILogger<UserTelegramUnlinkedCleanupHandler> _logger;

    public UserTelegramUnlinkedCleanupHandler(
        IUserLinkRepository userLinks,
        ILogger<UserTelegramUnlinkedCleanupHandler> logger)
    {
        _userLinks = userLinks;
        _logger = logger;
    }

    public async Task Handle(UserTelegramUnlinked evt, CancellationToken cancellationToken)
    {
        int removed = await _userLinks.RemoveByTelegramUserIdAsync(evt.TelegramUserId, cancellationToken);
        if (removed == 0)
        {
            _logger.LogDebug(
                "UserTelegramUnlinked cleanup: no UserLink found for TelegramUserId {TelegramUserId}",
                evt.TelegramUserId);
            return;
        }

        _logger.LogInformation(
            "Removed UserLink after user.telegram_unlinked (TelegramUserId={TelegramUserId}, UserId={UserId})",
            evt.TelegramUserId,
            evt.UserId);
    }
}
