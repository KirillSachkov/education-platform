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
/// <c>POST /notifications/admin/campaigns/email-login-notice/run</c> — запуск кампании
/// «вход теперь по почте» (#704) пользователям с GitHub-привязкой. Идемпотентна per-user
/// (фиксированный campaign GUID), повторный запуск пропускает уже-уведомлённых.
///
/// Доступ требует permission <c>Platform.ADMIN</c>.
/// </summary>
public sealed class RunEmailLoginNoticeCampaignEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/admin/campaigns/email-login-notice/run/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN)
            // Reuse the broadcast policy (5/60min) — same «accidental double mass-send»
            // hazard: each run fans out S2S + outbox work over the whole audience.
            .RequireRateLimiting(NotificationRateLimitPolicies.BROADCAST);
    }

    private static async Task<EndpointResult<RunCampaignResponse>> HandleAsync(
        [FromServices] IEmailLoginNoticeCampaignRunner runner,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.RunAsync(ct);
        return result.Map(queued => new RunCampaignResponse(queued));
    }
}