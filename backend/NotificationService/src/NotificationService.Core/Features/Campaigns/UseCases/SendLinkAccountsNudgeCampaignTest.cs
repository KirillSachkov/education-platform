using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Admin.Dtos;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Campaigns.UseCases;

/// <summary>
/// <c>POST /notifications/admin/campaigns/link-accounts-nudge/test</c> — тестовая отправка
/// ОДНОГО nudge «привяжите GitHub и Telegram» (#704) самому админу (в InApp-инбокс).
/// NON-campaign correlation (random) — не помечает админа уведомлённым в реальной кампании,
/// отправку можно повторять.
///
/// Auth — permission <c>Platform.ADMIN</c>; получатель = текущий пользователь.
/// </summary>
public sealed class SendLinkAccountsNudgeCampaignTestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/admin/campaigns/link-accounts-nudge/test/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN)
            // Same broadcast policy as /run — a test send still does dispatch work; the
            // shared limiter also guards against rapid-fire double sends.
            .RequireRateLimiting(NotificationRateLimitPolicies.BROADCAST);
    }

    private static async Task<EndpointResult<SendTestCampaignResponse>> HandleAsync(
        [FromServices] ILinkAccountsNudgeCampaignRunner runner,
        [FromServices] UserScopedData user,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.RunTestAsync(user.UserId, ct);
        return result.Map(_ => new SendTestCampaignResponse(Sent: true));
    }
}
