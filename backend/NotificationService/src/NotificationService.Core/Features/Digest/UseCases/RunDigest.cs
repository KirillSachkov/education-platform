using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Admin.Dtos;
using PlatformAuth.Authorization;

namespace NotificationService.Core.Features.Digest.UseCases;

/// <summary>
/// <c>POST /notifications/admin/digest/run/</c> — ручной запуск одного прохода еженедельного
/// дайджеста (#468): ops-триггер и опора интеграционных тестов. Выполняется немедленно,
/// минуя schedule-guard фонового <c>WeeklyDigestService</c>; per-user идемпотентность
/// (6-дневное окно) сохраняется внутри <see cref="IWeeklyDigestRunner"/>.
///
/// Auth — та же идиома, что у остальных admin-endpoint'ов сервиса
/// (<c>GetDeliveryStatsEndpoint</c>): permission <c>Platform.ADMIN</c> (роль platform-admin).
/// </summary>
public sealed class RunDigestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/admin/digest/run", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<RunDigestResponse>> HandleAsync(
        [FromServices] IWeeklyDigestRunner runner,
        CancellationToken ct)
    {
        int usersNotified = await runner.RunOnceAsync(ct);
        return new RunDigestResponse(usersNotified);
    }
}
