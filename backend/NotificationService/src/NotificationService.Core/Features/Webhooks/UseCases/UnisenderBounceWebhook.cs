using System.Text.Json.Serialization;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Database;
using PlatformAuth.Authorization;

namespace NotificationService.Core.Features.Webhooks.UseCases;

/// <summary>
/// <c>POST /webhooks/unisender</c> — webhook для Unisender Go статус-событий email-доставок.
///
/// <para>
/// Формат payload: JSON-объект с массивом <c>events_by_user</c>. Каждое событие содержит
/// <c>event_data.job_id</c> (соответствует <c>provider_message_id</c> в <c>notification_deliveries</c>)
/// и <c>event_data.status</c> — <c>sent</c> / <c>delivered</c> / <c>soft_bounced</c> / <c>hard_bounced</c>
/// / <c>spam</c> / <c>unsubscribed</c>.
/// </para>
///
/// <para>
/// Действия:
/// </para>
/// <list type="bullet">
///   <item><c>hard_bounced</c> / <c>spam</c> / <c>unsubscribed</c> → пометить delivery Failed
///     + отключить <c>EmailEnabled = false</c> у recipient (permanent fail).</item>
///   <item><c>soft_bounced</c> → пометить Failed, но канал оставить (временная проблема).</item>
///   <item><c>sent</c> / <c>delivered</c> → игнорируем (у нас уже Delivered после success SMTP).</item>
/// </list>
///
/// <para>
/// Auth: анонимный endpoint, т.к. webhooks приходят от внешнего провайдера. Защита через
/// опциональный shared secret в header <c>X-Unisender-Secret</c>; если конфиг
/// <c>Notifications:Unisender:WebhookSecret</c> задан — header обязателен.
/// </para>
/// </summary>
public sealed class UnisenderBounceWebhookEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/unisender", HandleAsync).AllowAnonymousEndpoint();
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> HandleAsync(
        [FromBody] UnisenderWebhookPayload payload,
        [FromServices] ITransactionManager transactions,
        [FromServices] IUserChannelsRepository userChannels,
        [FromServices] IOptions<NotificationOptions> options,
        [FromServices] ILogger<UnisenderBounceWebhookEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Shared secret validation — FAIL-CLOSED. Это анонимный МУТИРУЮЩИЙ webhook: spoof'нутый
        // bounce выключает email-канал произвольному юзеру (griefing). Раньше проверка была
        // opt-in (нет секрета → открыто), а prod не задавал секрет вовсе. Теперь без секрета
        // endpoint отклоняет всё. Чтобы включить bounce-обработку — задать
        // Notifications:Unisender:WebhookSecret (+ тот же секрет в кабинете Unisender Go).
        UnisenderWebhookOptions cfg = options.Value.Unisender;
        if (string.IsNullOrEmpty(cfg.WebhookSecret))
        {
            logger.LogWarning(
                "Unisender webhook rejected: WebhookSecret not configured (fail-closed). "
                + "Set Notifications:Unisender:WebhookSecret to enable bounce handling.");
            return Results.Unauthorized();
        }

        string? provided = httpContext.Request.Headers["X-Unisender-Secret"].ToString();
        if (!string.Equals(provided, cfg.WebhookSecret, StringComparison.Ordinal))
        {
            logger.LogWarning("Unisender webhook: invalid or missing secret header");
            return Results.Unauthorized();
        }

        if (payload?.EventsByUser is null || payload.EventsByUser.Count == 0)
            return Results.Ok();

        int processed = 0;
        foreach (UnisenderUserEvents batch in payload.EventsByUser)
        {
            foreach (UnisenderEvent evt in batch.Events ?? [])
            {
                if (evt.EventData is null)
                    continue;

                string? jobId = evt.EventData.JobId;
                string? status = evt.EventData.Status;
                string? email = evt.EventData.Email;

                if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(status))
                    continue;

                await ProcessEventAsync(
                    transactions, userChannels,
                    jobId, status, email, evt.EventData.ErrorDetails,
                    logger, ct);
                processed++;
            }
        }

        logger.LogInformation("Unisender webhook: processed {Count} events", processed);
        return Results.Ok(new { processed });
    }

    private static async Task ProcessEventAsync(
        ITransactionManager transactions,
        IUserChannelsRepository userChannels,
        string jobId,
        string status,
        string? email,
        string? errorDetails,
        ILogger logger,
        CancellationToken ct)
    {
        // Ignore positive statuses — в БД уже Delivered (после 200 OK от Unisender API).
        if (status is "sent" or "delivered" or "opened" or "clicked")
            return;

        bool isPermanent = status is "hard_bounced" or "spam" or "unsubscribed";

        // 1. Обновляем delivery → Failed. Используем raw SQL (Dapper) — ExecuteSql быстрее чем EF fetch-update.
        //    Lookup по `provider_message_id` покрыт partial index `ix_notification_deliveries_provider_message_id`
        //    (миграция 20260518_AddNotificationDeliveriesProviderMessageIdIndex, issue #230 DB-2).
        System.Data.Common.DbConnection conn = transactions.GetDbConnection();
        int updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE notifications.notification_deliveries
            SET status = 2,             -- DeliveryStatus.Failed
                error_code = @status,
                error_detail = @detail,
                completed_at = COALESCE(completed_at, timezone('utc', now()))
            WHERE provider_message_id = @jobId
              AND channel = 4           -- NotificationChannel.Email
            """,
            new { jobId, status, detail = errorDetails ?? "" },
            cancellationToken: ct));

        if (updated == 0)
        {
            logger.LogDebug("Unisender bounce: no matching delivery for jobId={JobId} status={Status}", jobId, status);
            return;
        }

        if (!isPermanent || string.IsNullOrWhiteSpace(email))
            return;

        // 2. Hard bounce / spam / unsubscribe → найти recipient и выключить email-канал.
        Guid? recipientId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT n.recipient_user_id
            FROM notifications.notification_deliveries d
            JOIN notifications.notifications n ON n.id = d.notification_id
            WHERE d.provider_message_id = @jobId
            LIMIT 1
            """,
            new { jobId }, cancellationToken: ct));

        if (recipientId is null)
        {
            logger.LogDebug("Unisender bounce: recipient not found for jobId={JobId}", jobId);
            return;
        }

        // Сохраняем текущие Telegram/WebPush-флаги (не трогаем) — читаем и повторяем.
        Domain.UserChannels.UserNotificationChannels? current =
            await userChannels.GetByUserIdAsync(recipientId.Value, ct);
        bool telegramEnabled = current?.TelegramEnabled ?? false;
        bool webPushEnabled = current?.WebPushEnabled ?? true;

        await userChannels.UpsertFlagsAsync(
            recipientId.Value,
            telegramEnabled: telegramEnabled,
            emailEnabled: false,
            webPushEnabled: webPushEnabled,
            cancellationToken: ct);

        logger.LogWarning(
            "Unisender {Status} bounce for user {UserId} ({Email}) — email auto-disabled",
            status, recipientId.Value, email);
    }

    // --- DTO (internal shape) ---

    public sealed class UnisenderWebhookPayload
    {
        [JsonPropertyName("events_by_user")]
#pragma warning disable CA1002, CA2227 // DTO for JSON deserialization — List<T> init-only is idiomatic
        public List<UnisenderUserEvents>? EventsByUser { get; init; }
#pragma warning restore CA1002, CA2227
    }

    public sealed class UnisenderUserEvents
    {
        [JsonPropertyName("user_id")]
        public long UserId { get; init; }

        [JsonPropertyName("events")]
#pragma warning disable CA1002, CA2227
        public List<UnisenderEvent>? Events { get; init; }
#pragma warning restore CA1002, CA2227
    }

    public sealed class UnisenderEvent
    {
        [JsonPropertyName("event_name")]
        public string? EventName { get; init; }

        [JsonPropertyName("event_data")]
        public UnisenderEventData? EventData { get; init; }
    }

    public sealed class UnisenderEventData
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("delivery_info")]
        public DeliveryInfo? DeliveryInfo { get; init; }

        [JsonIgnore]
        public string? ErrorDetails => DeliveryInfo?.Message;
    }

    public sealed class DeliveryInfo
    {
        [JsonPropertyName("delivery_status")]
        public string? DeliveryStatus { get; init; }

        [JsonPropertyName("destination_response")]
        public string? Message { get; init; }
    }
}
