using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Contracts.HttpCommunication;
using TelegramBotService.Core.Database;

namespace TelegramBotService.Core.Features.UserLinks.Handlers;

/// <summary>
///     Логика <c>/unlink</c>: полностью разрывает Telegram-привязку.
///
///     Step 1 — зовёт AuthService <c>POST /internal/telegram/unlink-by-telegram-id</c>,
///     который удаляет OIDC-логин в <c>auth.user_logins</c> и публикует
///     <c>UserTelegramUnlinked</c>. Идемпотентно — если в auth ничего нет, success.
///
///     Step 2 — удаляет локальный <see cref="Domain.UserLinks.UserLink"/>. Дублирующее
///     удаление через <c>UserTelegramUnlinkedCleanupHandler</c> по событию — fallback,
///     no-op после step 2.
///
///     Без step 1 bot-side cleanup оставлял бы осиротевший row в <c>auth.user_logins</c>,
///     блокирующий повторную привязку того же Telegram-аккаунта к другому платформенному
///     пользователю (409 telegram.link.already_linked_to_other в <c>VerifyTelegramLink</c>).
/// </summary>
public sealed class UnlinkHandler
{
    private readonly IAuthTelegramClient _authClient;
    private readonly IUserLinkRepository _userLinks;
    private readonly ITransactionManager _transactions;
    private readonly IBotNotifier _notifier;
    private readonly ILogger<UnlinkHandler> _logger;

    public UnlinkHandler(
        IAuthTelegramClient authClient,
        IUserLinkRepository userLinks,
        ITransactionManager transactions,
        IBotNotifier notifier,
        ILogger<UnlinkHandler> logger)
    {
        _authClient = authClient;
        _userLinks = userLinks;
        _transactions = transactions;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        long telegramUserId = ctx.UserId;

        // Step 1 — full OIDC unlink in AuthService (idempotent).
        UnitResult<Error> authResult = await _authClient.UnlinkByTelegramIdAsync(
            telegramUserId, ctx.CancellationToken);

        if (authResult.IsFailure)
        {
            _logger.LogWarning(
                "AuthService unlink failed for telegramUserId {TelegramUserId}: {Code} {Message}",
                telegramUserId,
                authResult.Error.Messages[0].Code,
                authResult.Error.Messages[0].Message);

            await _notifier.SendTextAsync(
                ctx.ChatId,
                "Не удалось отвязать аккаунт. Попробуйте позже.",
                ct: ctx.CancellationToken);

            return BotResults.Empty();
        }

        // Step 2 — bot-side local cleanup. Removed = 0 если link уже удалён consumer'ом
        // или его никогда не было — это норма после auth-side cleanup, не ошибка.
        int removed = await _userLinks.RemoveByTelegramUserIdAsync(
            telegramUserId, ctx.CancellationToken);

        if (removed > 0)
        {
            UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ctx.CancellationToken);
            if (saveResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to persist UserLink removal for telegramUserId {TelegramUserId}: {Code}",
                    telegramUserId,
                    saveResult.Error.Messages[0].Code);
                // Auth-side уже разорван — пользователю это важнее. Local row подчистит
                // UserTelegramUnlinkedCleanupHandler по приходящему event'у.
            }
        }

        await _notifier.SendTextAsync(
            ctx.ChatId,
            "Аккаунт полностью отвязан от платформы. " +
            "Если захотите снова получать уведомления — привяжите Telegram в настройках профиля.",
            ct: ctx.CancellationToken);

        return BotResults.Empty();
    }
}
