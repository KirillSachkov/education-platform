using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;

namespace NotificationService.Core.Channels.Telegram;

/// <summary>
/// Wolverine consumer события <see cref="TelegramDeliveryRecorded"/>: пишет результат
/// доставки Telegram-уведомления в <c>notification_deliveries</c> с
/// <c>channel = NotificationChannel.Telegram</c>. Закрывает «слепое пятно» — до этого
/// сервис публиковал <c>notification.created</c> в TG-канал и не имел никакого
/// фидбека о судьбе сообщения, поэтому за 7 дней prod'а в delivery_log было 0 записей
/// для channel=2.
///
/// Handler идемпотентен: при Wolverine retry того же event'a обновляет существующую
/// запись (status/error_code/provider_message_id) вместо создания дубля.
/// </summary>
public sealed class TelegramDeliveryRecordedHandler
{
    private readonly IDeliveriesRepository _deliveries;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<TelegramDeliveryRecordedHandler> _logger;

    public TelegramDeliveryRecordedHandler(
        IDeliveriesRepository deliveries,
        ITransactionManager transactions,
        ILogger<TelegramDeliveryRecordedHandler> logger)
    {
        _deliveries = deliveries;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task Handle(TelegramDeliveryRecorded evt, CancellationToken ct)
    {
        NotificationId id = NotificationId.Of(evt.NotificationId);

        UnitResult<Error> result = await UpsertDeliveryAsync(id, evt, ct);

        // Race с concurrent retry'ём того же event'a: оба прошли GetForNotificationAsync
        // → null, оба сделали INSERT, второй упал unique-violation
        // (ux_notification_deliveries_notification_channel). TransactionManager мапит
        // в delivery.already.exists. Делаем второй проход — теперь существующая запись
        // там, идём в UPDATE-ветку.
        if (result.IsFailure
            && string.Equals(result.Error.Messages[0].Code, "delivery.already.exists", StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Concurrent retry detected for Telegram delivery {NotificationId} → chat {ChatId}; retrying as update",
                evt.NotificationId, evt.ChatId);

            result = await UpsertDeliveryAsync(id, evt, ct);
        }

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to persist Telegram delivery for {NotificationId} → chat {ChatId}: {Code}",
                evt.NotificationId, evt.ChatId, result.Error.Messages[0].Code);
        }
    }

    private async Task<UnitResult<Error>> UpsertDeliveryAsync(
        NotificationId id,
        TelegramDeliveryRecorded evt,
        CancellationToken ct)
    {
        NotificationDelivery? existing = await _deliveries
            .GetForNotificationAsync(id, NotificationChannel.Telegram, ct);

        NotificationDelivery delivery;
        if (existing is not null)
        {
            delivery = existing;
        }
        else
        {
            Result<NotificationDelivery, Error> created =
                NotificationDelivery.Create(id, NotificationChannel.Telegram);

            if (created.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to create Telegram delivery record for {NotificationId}: {Error}",
                    evt.NotificationId, created.Error.Messages[0].Code);
                return UnitResult.Success<Error>();
            }

            delivery = created.Value;
            await _deliveries.AddAsync(delivery, ct);
        }

        ApplyOutcome(delivery, evt);

        return await _transactions.SaveChangesAsync(ct);
    }

    private static void ApplyOutcome(NotificationDelivery delivery, TelegramDeliveryRecorded evt)
    {
        switch (evt.Status)
        {
            case TelegramDeliveryStatuses.DELIVERED:
                delivery.MarkDelivered(evt.ProviderMessageId);
                break;
            case TelegramDeliveryStatuses.SKIPPED:
                delivery.MarkSkipped(evt.ErrorCode ?? "unknown", evt.ErrorDetail);
                break;
            case TelegramDeliveryStatuses.FAILED:
                delivery.MarkFailed(evt.ErrorCode ?? "unknown", evt.ErrorDetail ?? string.Empty);
                break;
            default:
                // Forward-compat: новый статус, который мы ещё не знаем — пишем как failed
                // с error_code=unknown_status, чтобы запись осталась.
                delivery.MarkFailed("unknown_status", evt.Status);
                break;
        }
    }
}
