using System.Data.Common;
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
/// <c>GET /admin/notifications/stats/</c> — агрегированная статистика доставок за период.
///
/// <para>Диапазон дат по умолчанию: последние 7 дней. Максимум — 90 дней
/// (anti-DoS, issue #236 NEW-2). Dapper выполняет 3 aggregate'а отдельными
/// запросами в рамках одного Connection, BRIN index на <c>created_at</c>
/// (issue #236 DB-3) держит стоимость сканов под контролем.</para>
/// </summary>
public sealed class GetDeliveryStatsEndpoint : IEndpoint
{
    private const int MAX_DATE_RANGE_DAYS = 90;
    private const int QUERY_TIMEOUT_SECONDS = 30;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/notifications/stats", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<DeliveryStatsResponse>> HandleAsync(
        [FromServices] ITransactionManager transactionManager,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        CancellationToken ct)
    {
        // Defaults — последние 7 дней. Эти же значения участвуют в проверке ширины,
        // чтобы guard работал и для частично-заданного диапазона (только from / только to).
        DateTime toUtc = (dateTo ?? DateTimeOffset.UtcNow).UtcDateTime;
        DateTime fromUtc = (dateFrom ?? new DateTimeOffset(toUtc, TimeSpan.Zero).AddDays(-7)).UtcDateTime;

        // Guard: одинаковая семантика и код ошибки с `ListDeliveriesEndpoint` —
        // фронт уже отрабатывает `admin.deliveries.date_range_too_wide` и сможет
        // переиспользовать UI message.
        if (fromUtc > toUtc)
        {
            return Result.Failure<DeliveryStatsResponse, Error>(Error.Validation(
                "admin.deliveries.date_range_invalid",
                "Начало диапазона дат не может быть позже конца"));
        }
        if ((toUtc - fromUtc).TotalDays > MAX_DATE_RANGE_DAYS)
        {
            return Result.Failure<DeliveryStatsResponse, Error>(Error.Validation(
                "admin.deliveries.date_range_too_wide",
                $"Диапазон дат не может превышать {MAX_DATE_RANGE_DAYS} дней"));
        }

        DbConnection conn = transactionManager.GetDbConnection();

        IEnumerable<ChannelStatusBucket> perChannel = await conn.QueryAsync<ChannelStatusBucket>(
            new CommandDefinition(
                """
                SELECT d.channel, d.status, COUNT(*)::bigint AS count
                FROM notifications.notification_deliveries d
                WHERE d.created_at BETWEEN @fromUtc AND @toUtc
                GROUP BY d.channel, d.status
                ORDER BY d.channel, d.status
                """,
                new { fromUtc, toUtc },
                commandTimeout: QUERY_TIMEOUT_SECONDS,
                cancellationToken: ct));

        IEnumerable<TypeBucket> perType = await conn.QueryAsync<TypeBucket>(
            new CommandDefinition(
                """
                SELECT n.type, COUNT(*)::bigint AS count
                FROM notifications.notification_deliveries d
                JOIN notifications.notifications n ON n.id = d.notification_id
                WHERE d.created_at BETWEEN @fromUtc AND @toUtc
                GROUP BY n.type
                ORDER BY count DESC
                """,
                new { fromUtc, toUtc },
                commandTimeout: QUERY_TIMEOUT_SECONDS,
                cancellationToken: ct));

        IEnumerable<FailureReason> topFailures = await conn.QueryAsync<FailureReason>(
            new CommandDefinition(
                """
                SELECT COALESCE(d.error_code, 'unknown') AS errorcode, COUNT(*)::bigint AS count
                FROM notifications.notification_deliveries d
                WHERE d.created_at BETWEEN @fromUtc AND @toUtc
                  AND d.status = 2  -- DeliveryStatus.Failed
                GROUP BY d.error_code
                ORDER BY count DESC
                LIMIT 10
                """,
                new { fromUtc, toUtc },
                commandTimeout: QUERY_TIMEOUT_SECONDS,
                cancellationToken: ct));

        return new DeliveryStatsResponse
        {
            PerChannel = [.. perChannel],
            PerType = [.. perType],
            TopFailures = [.. topFailures],
        };
    }
}
