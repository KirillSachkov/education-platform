using System.Data.Common;
using System.Text;
using Core.Database;
using CSharpFunctionalExtensions;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Admin.Dtos;
using PlatformAuth.Authorization;
using SharedKernel;

namespace NotificationService.Core.Features.Admin.UseCases;

/// <summary>
/// <c>GET /admin/notifications/deliveries/</c> — paged list доставок для admin-панели.
///
/// <para>Filters (query params):</para>
/// <list type="bullet">
///   <item><c>status</c> — short (0..3) — Pending/Delivered/Failed/Skipped</item>
///   <item><c>channel</c> — short (1/2/4) — InApp/Telegram/Email</item>
///   <item><c>recipientUserId</c> — Guid — поиск по конкретному получателю</item>
///   <item><c>dateFrom</c>, <c>dateTo</c> — ISO-8601</item>
///   <item><c>cursorBefore</c>, <c>cursorId</c> — keyset pagination (created_at DESC, id DESC)</item>
///   <item><c>limit</c> — int, default 50, max 200</item>
/// </list>
///
/// <para>Только <c>Platform.ADMIN</c>. Dapper JOIN `notification_deliveries` ← `notifications`
/// чтобы получить <c>type</c> и <c>recipient_user_id</c> в одной выдаче без N+1.</para>
/// </summary>
public sealed class ListDeliveriesEndpoint : IEndpoint
{
    private const int DEFAULT_LIMIT = 50;
    private const int MAX_LIMIT = 200;
    private const int MAX_DATE_RANGE_DAYS = 90;
    private const int QUERY_TIMEOUT_SECONDS = 30;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/notifications/deliveries", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<DeliveryListResponse>> HandleAsync(
        [FromServices] ITransactionManager transactionManager,
        [FromQuery] short? status,
        [FromQuery] short? channel,
        [FromQuery] Guid? recipientUserId,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] DateTimeOffset? cursorBefore,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        int effectiveLimit = Math.Clamp(limit ?? DEFAULT_LIMIT, 1, MAX_LIMIT);

        // Date range guard (issue #230, PROD-2): без верхней границы admin может задеть
        // full-table scan через `dateFrom=MinValue`. Дефолт по полю = последние 90 дней
        // если не задан явно; явный диапазон проверяем на ширину.
        DateTime nowUtc = DateTime.UtcNow;
        DateTime to = dateTo?.UtcDateTime ?? nowUtc;
        DateTime from = dateFrom?.UtcDateTime
                        ?? (dateTo.HasValue ? to.AddDays(-MAX_DATE_RANGE_DAYS) : nowUtc.AddDays(-MAX_DATE_RANGE_DAYS));
        if (from > to)
        {
            return Result.Failure<DeliveryListResponse, Error>(Error.Validation(
                "admin.deliveries.date_range_invalid",
                "Начало диапазона дат не может быть позже конца"));
        }
        if ((to - from).TotalDays > MAX_DATE_RANGE_DAYS)
        {
            return Result.Failure<DeliveryListResponse, Error>(Error.Validation(
                "admin.deliveries.date_range_too_wide",
                $"Диапазон дат не может превышать {MAX_DATE_RANGE_DAYS} дней"));
        }

        if (cursorBefore.HasValue != cursorId.HasValue
            || (cursorId.HasValue && cursorId.Value == Guid.Empty))
        {
            return Result.Failure<DeliveryListResponse, Error>(Error.Validation(
                "admin.deliveries.cursor_invalid",
                "CursorBefore и непустой CursorId должны быть заданы вместе"));
        }

        StringBuilder sql = new(
            """
            SELECT
                d.id                  AS deliveryid,
                d.notification_id     AS notificationid,
                n.recipient_user_id   AS recipientuserid,
                n.type                AS type,
                n.template_id         AS templateid,
                d.channel             AS channel,
                d.status              AS status,
                d.provider_message_id AS providermessageid,
                d.error_code          AS errorcode,
                d.error_detail        AS errordetail,
                d.created_at          AS createdat,
                d.completed_at        AS completedat
            FROM notifications.notification_deliveries d
            JOIN notifications.notifications n ON n.id = d.notification_id
            WHERE 1 = 1
            """);

        DynamicParameters p = new();

        if (status.HasValue) { sql.Append(" AND d.status = @status"); p.Add("status", status.Value); }
        if (channel.HasValue) { sql.Append(" AND d.channel = @channel"); p.Add("channel", channel.Value); }
        if (recipientUserId.HasValue) { sql.Append(" AND n.recipient_user_id = @recipientUserId"); p.Add("recipientUserId", recipientUserId.Value); }
        sql.Append(" AND d.created_at >= @dateFrom AND d.created_at <= @dateTo");
        p.Add("dateFrom", from);
        p.Add("dateTo", to);
        if (cursorBefore.HasValue && cursorId.HasValue)
        {
            sql.Append(" AND (d.created_at < @cursorTs OR (d.created_at = @cursorTs AND d.id < @cursorId))");
            p.Add("cursorTs", cursorBefore.Value.UtcDateTime);
            p.Add("cursorId", cursorId.Value);
        }

        // Fetch limit+1 чтобы понять, есть ли следующая страница без второго запроса.
        sql.Append(" ORDER BY d.created_at DESC, d.id DESC LIMIT @limitPlusOne");
        p.Add("limitPlusOne", effectiveLimit + 1);

        DbConnection conn = transactionManager.GetDbConnection();
        IEnumerable<DeliveryListItemDto> rows = await conn.QueryAsync<DeliveryListItemDto>(
            new CommandDefinition(
                sql.ToString(),
                p,
                commandTimeout: QUERY_TIMEOUT_SECONDS,
                cancellationToken: ct));
        List<DeliveryListItemDto> all = [.. rows];

        bool hasMore = all.Count > effectiveLimit;
        List<DeliveryListItemDto> page = hasMore ? all.GetRange(0, effectiveLimit) : all;

        DeliveryListItemDto? last = hasMore ? page[^1] : null;

        return new DeliveryListResponse
        {
            Items = page,
            NextCursorBefore = last?.CreatedAt,
            NextCursorId = last?.DeliveryId,
        };
    }
}
