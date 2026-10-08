using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Admin.Dtos;
using PlatformAuth.Authorization;
using SharedKernel;

namespace NotificationService.Core.Features.Campaigns.UseCases;

/// <summary>
/// <c>POST /notifications/admin/campaigns/level-test-invite/run</c> — запуск кампании-рассылки
/// приглашений пройти публичный тест уровня ВСЕМ пользователям (#554). Идемпотентна per-user
/// (фиксированный campaign GUID), повторный запуск пропускает уже-приглашённых.
///
/// Auth — та же идиома, что у остальных admin-endpoint'ов сервиса: permission
/// <c>Platform.ADMIN</c> (роль platform-admin).
/// </summary>
public sealed class RunLevelTestInviteCampaignEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/admin/campaigns/level-test-invite/run/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN)
            // Reuse the broadcast policy (5/60min) — same «accidental double mass-send»
            // hazard: each run fans out S2S + outbox work over ALL users.
            .RequireRateLimiting(NotificationRateLimitPolicies.BROADCAST);
    }

    private static async Task<EndpointResult<RunCampaignResponse>> HandleAsync(
        [FromServices] ILevelTestInviteCampaignRunner runner,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.RunAsync(ct);
        return result.Map(queued => new RunCampaignResponse(queued));
    }
}
