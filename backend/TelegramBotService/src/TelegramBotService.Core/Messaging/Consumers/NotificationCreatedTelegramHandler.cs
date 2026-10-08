using System.Text.RegularExpressions;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Delivery;
using TelegramBotService.Core.Diagnostics;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     Wolverine handler — на <c>NotificationCreated</c> делегирует доставку в
///     <see cref="ITelegramDeliveryService"/>, который сам:
///     <list type="bullet">
///     <item>Чекает Redis-идемпотентность (защита от дублей при Wolverine retry).</item>
///     <item>Публикует <c>notification.telegram_delivery_recorded</c> для каждого outcome'а
///     (delivered/failed/skipped) — closes слепое пятно по доставке.</item>
///     <item>Классифицирует ошибки Telegram API в стабильные error_code'ы.</item>
///     </list>
///     Handler отвечает только за: проверку Telegram-бита в channels, поиск UserLink,
///     извлечение trailing markdown link → InlineKeyboard URL button, и cleanup UserLink
///     при <c>bot_blocked</c> / <c>chat_not_found</c>.
/// </summary>
public sealed partial class NotificationCreatedTelegramHandler
{
    private readonly IUserLinkRepository _userLinks;
    private readonly ITransactionManager _transactions;
    private readonly ITelegramDeliveryService _delivery;
    private readonly TelegramMetrics _metrics;
    private readonly ILogger<NotificationCreatedTelegramHandler> _logger;

    public NotificationCreatedTelegramHandler(
        IUserLinkRepository userLinks,
        ITransactionManager transactions,
        ITelegramDeliveryService delivery,
        TelegramMetrics metrics,
        ILogger<NotificationCreatedTelegramHandler> logger)
    {
        _userLinks = userLinks;
        _transactions = transactions;
        _delivery = delivery;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(NotificationCreated evt, CancellationToken cancellationToken)
    {
        if ((evt.Channels & TelegramBotConstants.TELEGRAM_CHANNEL_BIT) != TelegramBotConstants.TELEGRAM_CHANNEL_BIT)
            return;

        // Лаг от создания нотификации до начала обработки в TG-боте. Покрывает outbox flush
        // в NotificationService + RabbitMQ routing + listener pickup. Скачок здесь = очередь
        // забилась (мало listener'ов или fan-out большой).
        _metrics.RecordHandlerLag(DateTimeOffset.UtcNow - evt.CreatedAt);

        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == evt.RecipientUserId,
            cancellationToken);

        if (linkResult.IsFailure)
        {
            _logger.LogDebug(
                "User {UserId} has no Telegram link; recording skipped delivery for notification {NotificationId}",
                evt.RecipientUserId,
                evt.NotificationId);

            // Раньше был silent return — пользователь не знал, что было уведомление,
            // которое не дошло. Теперь публикуем skip-event → NotificationService пишет
            // запись в notification_deliveries с error_code=no_user_link, UI может
            // подсветить «есть уведомление, но Telegram не настроен».
            await _delivery.RecordSkipAsync(
                notificationId: evt.NotificationId,
                recipientUserId: evt.RecipientUserId,
                chatId: 0,
                errorCode: TelegramDeliveryErrorCodes.NO_USER_LINK,
                errorDetail: null,
                ct: cancellationToken).ConfigureAwait(false);
            return;
        }

        UserLink link = linkResult.Value;

        // Soft-block: link заблокирован после ранее полученного permanent error
        // (bot_blocked / chat_not_found). Не пытаемся слать — публикуем skip event,
        // на котором UI/метрики могут понять «доставка не пошла, бот заблокирован».
        // Юзер вернётся через /start → LinkAccountHandler вызовет Unblock и след.
        // notification пойдёт нормально (без recreate link entry).
        if (!link.IsActive)
        {
            _logger.LogDebug(
                "User {UserId} has soft-blocked Telegram link ({Reason}); recording skip for {NotificationId}",
                evt.RecipientUserId, link.BlockedReason, evt.NotificationId);

            await _delivery.RecordSkipAsync(
                notificationId: evt.NotificationId,
                recipientUserId: evt.RecipientUserId,
                chatId: link.TelegramUserId,
                errorCode: TelegramDeliveryErrorCodes.BLOCKED_LINK,
                errorDetail: link.BlockedReason,
                ct: cancellationToken).ConfigureAwait(false);
            return;
        }

        if (evt.TelegramBody is not { Length: > 0 } text)
        {
            // Не шлём raw InApp-Title/Body в Telegram: они содержат plain text без MarkdownV1-escape
            // user-values и могут сломать парсинг (или хуже — открыть injection-вектор).
            // Если NotificationService выставил Telegram-бит, но TelegramBody пустой — это баг
            // на той стороне; логируем warning и пишем skip-event.
            _logger.LogWarning(
                "Notification {NotificationId} has Telegram channel bit set but TelegramBody is empty; recording skipped delivery",
                evt.NotificationId);

            await _delivery.RecordSkipAsync(
                notificationId: evt.NotificationId,
                recipientUserId: evt.RecipientUserId,
                chatId: link.TelegramUserId,
                errorCode: TelegramDeliveryErrorCodes.EMPTY_BODY,
                errorDetail: null,
                ct: cancellationToken).ConfigureAwait(false);
            return;
        }

        (string body, InlineKeyboardMarkup? keyboard) = ExtractTrailingLinkAsButton(text);

        TelegramDeliveryOutcome outcome = await _delivery.DeliverAsync(
            notificationId: evt.NotificationId,
            recipientUserId: evt.RecipientUserId,
            chatId: link.TelegramUserId,
            text: body,
            keyboard: keyboard,
            parseMode: ParseMode.Markdown,
            ct: cancellationToken).ConfigureAwait(false);

        if (outcome.IsFailed
            && string.Equals(outcome.ErrorCode, TelegramDeliveryErrorCodes.FLOOD_CONTROL, StringComparison.Ordinal))
        {
            throw Error.Failure(
                    "telegram.delivery.rate_limited",
                    outcome.ErrorDetail ?? "Telegram rate limit reached")
                .AsTransient()
                .ToException();
        }

        // Soft-block UserLink при permanent ошибках. Раньше делали полное delete —
        // юзер разблокировал бота → новый link recreate'ить нельзя без token'а из
        // /settings/integrations, плохой UX. Теперь Block(reason): link остаётся,
        // следующие notification'ы skip'ятся (см. !IsActive ветку выше), а при
        // /start от того же telegram_user_id LinkAccountHandler вызовет Unblock
        // и доставка возобновится автоматически.
        if (outcome.IsFailed && IsPermanentLinkError(outcome.ErrorCode))
        {
            _logger.LogInformation(
                "Telegram link soft-blocked for user {UserId} (TelegramUserId={TelegramUserId}, reason={Code}). Awaiting /start to unblock.",
                link.PlatformUserId,
                link.TelegramUserId,
                outcome.ErrorCode);

            link.Block(outcome.ErrorCode!);
            UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to persist UserLink soft-block for {TelegramUserId}: {Code}",
                    link.TelegramUserId,
                    saveResult.Error.Messages[0].Code);
                throw saveResult.Error.AsTransient().ToException();
            }
        }
    }

    /// <summary>
    /// Permanent ошибки UserLink: <c>bot_blocked</c> и <c>chat_not_found</c>. Не путать
    /// с <c>flood_control</c> (transient — link оставляем) и <c>api_error</c> (5xx — link оставляем).
    /// </summary>
    private static bool IsPermanentLinkError(string? errorCode) =>
        string.Equals(errorCode, TelegramDeliveryErrorCodes.BOT_BLOCKED, StringComparison.Ordinal) ||
        string.Equals(errorCode, TelegramDeliveryErrorCodes.CHAT_NOT_FOUND, StringComparison.Ordinal);

    // Превращает trailing markdown-ссылку в шаблоне в InlineKeyboard URL-кнопку — явная
    // кнопка под сообщением заметнее inline-link подчёркивания, особенно на mobile.
    private static (string Body, InlineKeyboardMarkup? Keyboard) ExtractTrailingLinkAsButton(string text)
    {
        Match match = TrailingMarkdownLink().Match(text);
        if (!match.Success)
            return (text, null);

        string label = match.Groups["text"].Value.Trim();
        string url = match.Groups["url"].Value.Trim();

        if (label.Length == 0 || url.Length == 0)
            return (text, null);

        // Telegram Bot API отвергает inline keyboard URL'ы с приватным хостом
        // (localhost / 127.0.0.1) ошибкой 400 "Wrong HTTP URL", и весь sendMessage
        // падает целиком — пользователь не получает даже текстовое уведомление.
        // На dev FrontendBaseUrl=http://localhost, поэтому fallback'имся: оставляем
        // trailing markdown-link встроенным в текст (MarkdownV1 его рендерит как
        // обычную inline-ссылку), и сообщение хотя бы доходит.
        if (!IsAllowedTelegramButtonUrl(url))
            return (text, null);

        string body = text[..match.Index].TrimEnd();
        InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithUrl(label, url));
        return (body, keyboard);
    }

    private static bool IsAllowedTelegramButtonUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        string host = uri.Host;
        return !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\s*\[(?<text>[^\]\n]+)\]\((?<url>https?://[^\s\)]+)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingMarkdownLink();
}
