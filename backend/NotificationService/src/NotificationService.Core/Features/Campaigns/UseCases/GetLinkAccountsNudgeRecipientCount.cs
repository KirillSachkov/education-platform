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
/// <c>GET /notifications/admin/campaigns/link-accounts-nudge/recipient-count</c> — размер
/// адресуемой аудитории кампании «привяжите GitHub и Telegram» (#704): сумма всех id
/// пользователей БЕЗ GitHub-привязки из keyset-пагинации AuthService. Opt-out фильтруется
/// на доставке, не здесь.
///
/// Auth — permission <c>Platform.ADMIN</c>.
/// </summary>
public sealed class GetLinkAccountsNudgeRecipientCountEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/admin/campaigns/link-accounts-nudge/recipient-count/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<CampaignRecipientCountResponse>> HandleAsync(
        [FromServices] ILinkAccountsNudgeCampaignRunner runner,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.CountRecipientsAsync(ct);
        return result.Map(count => new CampaignRecipientCountResponse(count));
    }
}
