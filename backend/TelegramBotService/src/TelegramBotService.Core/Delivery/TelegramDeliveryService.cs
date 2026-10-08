using System.Diagnostics;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Diagnostics;

namespace TelegramBotService.Core.Delivery;

/// <inheritdoc cref="ITelegramDeliveryService"/>
public sealed class TelegramDeliveryService : ITelegramDeliveryService
{
    private readonly IBotNotifier _notifier;
    private readonly ITelegramIdempotencyStore _idempotency;
    private readonly IBotThrottler _throttler;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly TelegramMetrics _metrics;
    private readonly ILogger<TelegramDeliveryService> _logger;

    public TelegramDeliveryService(
        IBotNotifier notifier,
        ITelegramIdempotencyStore idempotency,
        IBotThrottler throttler,
        IOutboxService outbox,
        ITransactionManager transactions,
        TelegramMetrics metrics,
        ILogger<TelegramDeliveryService> logger)
    {
        _notifier = notifier;
        _idempotency = idempotency;
        _throttler = throttler;
        _outbox = outbox;
        _transactions = transactions;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<TelegramDeliveryOutcome> DeliverAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        string text,
        InlineKeyboardMarkup? keyboard,
        ParseMode parseMode,
        CancellationToken ct)
    {
        // 1. Idempotency check — не отправлять повторно. Главная защита от дублей при
        //    Wolverine retry handler'а после успешной отправки в Telegram.
        int? cached = await _idempotency.TryGetSentMessageIdAsync(notificationId, chatId, ct).ConfigureAwait(false);
        if (cached is { } messageId)
        {
            _logger.LogDebug(
                "Telegram idempotency hit for notification {NotificationId} → chat {ChatId}, cached message_id={MessageId}",
                notificationId, chatId, messageId);

            TelegramDeliveryOutcome outcome = TelegramDeliveryOutcome.AlreadySent(messageId);
            await PublishOutcomeAsync(notificationId, recipientUserId, chatId, outcome, ct).ConfigureAwait(false);
            return outcome;
        }

        // 2. Per-chat throttle gate: enforces ≥1s между sends в один и тот же chat
        //    (Telegram Bot API: 1 msg/sec/DM). Token disposed после блока try/catch
        //    через `await using`, фиксируя lastSentAt.
        await using IAsyncDisposable _ = await _throttler.AcquireAsync(chatId, ct).ConfigureAwait(false);

        // 3. Real send.
        long sendStart = Stopwatch.GetTimestamp();
        try
        {
            Message sent = await _notifier.SendTextAsync(
                chatId: chatId,
                text: text,
                keyboard: keyboard,
                parseMode: parseMode,
                ct: ct).ConfigureAwait(false);

            _metrics.RecordSend(
                Stopwatch.GetElapsedTime(sendStart),
                outcome: TelegramDeliveryStatuses.DELIVERED,
                errorCode: null);

            // 3. Save to idempotency store BEFORE publishing event. Если упадём после
            //    SendMessage но до Save — следующий retry увидит cache miss и отправит
            //    повторно (рассогласование Telegram ↔ кэш). Чтобы это минимизировать,
            //    кэш пишем сразу.
            await _idempotency
                .SaveSentMessageIdAsync(notificationId, chatId, sent.MessageId, ct)
                .ConfigureAwait(false);

            TelegramDeliveryOutcome outcome = TelegramDeliveryOutcome.Delivered(sent.MessageId);
            await PublishOutcomeAsync(notificationId, recipientUserId, chatId, outcome, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Telegram delivered notification {NotificationId} → chat {ChatId}, message_id={MessageId}",
                notificationId, chatId, sent.MessageId);

            return outcome;
        }
        catch (ApiRequestException ex) when (IsBotBlocked(ex))
        {
            _metrics.RecordSend(Stopwatch.GetElapsedTime(sendStart), TelegramDeliveryStatuses.FAILED, TelegramDeliveryErrorCodes.BOT_BLOCKED);
            return await PublishFailureAsync(
                notificationId, recipientUserId, chatId,
                TelegramDeliveryErrorCodes.BOT_BLOCKED, ex.Message, ct).ConfigureAwait(false);
        }
        catch (ApiRequestException ex) when (IsChatNotFound(ex))
        {
            _metrics.RecordSend(Stopwatch.GetElapsedTime(sendStart), TelegramDeliveryStatuses.FAILED, TelegramDeliveryErrorCodes.CHAT_NOT_FOUND);
            return await PublishFailureAsync(
                notificationId, recipientUserId, chatId,
                TelegramDeliveryErrorCodes.CHAT_NOT_FOUND, ex.Message, ct).ConfigureAwait(false);
        }
        catch (ApiRequestException ex) when (IsFloodControl(ex))
        {
            _metrics.RecordSend(Stopwatch.GetElapsedTime(sendStart), TelegramDeliveryStatuses.FAILED, TelegramDeliveryErrorCodes.FLOOD_CONTROL);
            return await PublishFailureAsync(
                notificationId, recipientUserId, chatId,
                TelegramDeliveryErrorCodes.FLOOD_CONTROL, ex.Message, ct).ConfigureAwait(false);
        }
        catch (ApiRequestException ex)
        {
            _metrics.RecordSend(Stopwatch.GetElapsedTime(sendStart), TelegramDeliveryStatuses.FAILED, TelegramDeliveryErrorCodes.API_ERROR);
            return await PublishFailureAsync(
                notificationId, recipientUserId, chatId,
                TelegramDeliveryErrorCodes.API_ERROR,
                $"telegram_api_error_code={ex.ErrorCode}: {ex.Message}", ct).ConfigureAwait(false);
        }
    }

    public async Task RecordSkipAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        string errorCode,
        string? errorDetail,
        CancellationToken ct)
    {
        TelegramDeliveryOutcome outcome = TelegramDeliveryOutcome.Skipped(errorCode, errorDetail);
        await PublishOutcomeAsync(notificationId, recipientUserId, chatId, outcome, ct).ConfigureAwait(false);
    }

    private async Task<TelegramDeliveryOutcome> PublishFailureAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        string errorCode,
        string errorDetail,
        CancellationToken ct)
    {
        TelegramDeliveryOutcome outcome = TelegramDeliveryOutcome.Failed(errorCode, errorDetail);

        _logger.LogWarning(
            "Telegram delivery failed for notification {NotificationId} → chat {ChatId}: {ErrorCode} / {ErrorDetail}",
            notificationId, chatId, errorCode, errorDetail);

        await PublishOutcomeAsync(notificationId, recipientUserId, chatId, outcome, ct).ConfigureAwait(false);
        return outcome;
    }

    private async Task PublishOutcomeAsync(
        Guid notificationId,
        Guid recipientUserId,
        long chatId,
        TelegramDeliveryOutcome outcome,
        CancellationToken ct)
    {
        string status = outcome.Kind switch
        {
            TelegramDeliveryOutcomeKind.Delivered => TelegramDeliveryStatuses.DELIVERED,
            TelegramDeliveryOutcomeKind.Skipped => TelegramDeliveryStatuses.SKIPPED,
            TelegramDeliveryOutcomeKind.Failed => TelegramDeliveryStatuses.FAILED,
            _ => TelegramDeliveryStatuses.FAILED
        };

        _metrics.RecordOutcome(status, outcome.ErrorCode);

        var evt = new TelegramDeliveryRecorded(
            NotificationId: notificationId,
            RecipientUserId: recipientUserId,
            ChatId: chatId,
            Status: status,
            ProviderMessageId: outcome.ProviderMessageId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ErrorCode: outcome.ErrorCode,
            ErrorDetail: outcome.ErrorDetail,
            RecordedAt: DateTimeOffset.UtcNow);

        await _outbox.PublishAsync(evt).ConfigureAwait(false);
        // SaveChangesAsync flush'ит outbox (см. docs/agents/wolverine-tests.md).
        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct).ConfigureAwait(false);
        if (saveResult.IsFailure)
            throw saveResult.Error.AsTransient().ToException();
    }

    // 403 + "bot was blocked" — user заблокировал бота.
    private static bool IsBotBlocked(ApiRequestException ex) =>
        ex.ErrorCode == 403 &&
        ex.Message.Contains("bot was blocked", StringComparison.OrdinalIgnoreCase);

    // 400 + "chat not found" — telegram_user_id невалиден или бот не в чате.
    private static bool IsChatNotFound(ApiRequestException ex) =>
        ex.ErrorCode == 400 &&
        ex.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase);

    // 429 — rate-limit Telegram API. TBF Polly уже сделал MaxRetryOnRateLimit попыток,
    // если дошло сюда — retry'и исчерпаны.
    private static bool IsFloodControl(ApiRequestException ex) => ex.ErrorCode == 429;
}
