using System.Globalization;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace MaterialProcessingService.Core.Features.AdminInsights;

/// <summary>
///     Admin-only сводка по AI-pipeline'у: сколько job'ов прогнали за последние N дней,
///     по каким моделям и с каким исходом. Это основа для cost-аналитики до тех пор,
///     пока в схему не залит трекинг tokens/seconds per job (тогда добавим колонки и
///     перепишем агрегацию через них).
///
///     Сейчас цена считается приблизительно: count(job) × ориентировочная цена в ₽
///     (захардкожена в админ-UI, чтобы менять без миграций).
/// </summary>
public sealed class GetAiUsageEndpoint : IEndpoint
{
    private const int MAX_DAYS = 90;
    private const int DEFAULT_DAYS = 7;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/material-processing/admin/ai-usage",
                async Task<IResult> (
                    [FromQuery] int? days,
                    [FromServices] IAiUsageQueryService queryService,
                    CancellationToken cancellationToken) =>
                {
                    int window = Math.Clamp(days ?? DEFAULT_DAYS, 1, MAX_DAYS);
                    DateTime since = DateTime.UtcNow.AddDays(-window);

                    AiUsageSnapshot snapshot = await queryService.GetUsageAsync(since, cancellationToken);

                    return Results.Ok(new GetAiUsageResponse(
                        SinceUtc: since.ToString("O", CultureInfo.InvariantCulture),
                        Days: window,
                        TranscriptsCreated: snapshot.TranscriptsCreated,
                        Rows: snapshot.Rows));
                })
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }
}

public sealed record GetAiUsageResponse(
    string SinceUtc,
    int Days,
    int TranscriptsCreated,
    IReadOnlyList<AiUsageRowDto> Rows);

public sealed record AiUsageRowDto(
    string Day,
    string JobKind,
    string Model,
    string Status,
    int Count);
