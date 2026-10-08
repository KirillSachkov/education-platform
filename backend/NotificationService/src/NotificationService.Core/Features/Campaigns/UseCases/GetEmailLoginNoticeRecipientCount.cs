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
/// <c>GET /notifications/admin/campaigns/email-login-notice/recipient-count</c> — размер
/// адресуемой аудитории кампании «вход теперь по почте» (#704): сумма всех id пользователей
/// С GitHub-привязкой из keyset-пагинации AuthService.
///
/// Auth — permission <c>Platform.ADMIN</c>.
/// </summary>
public sealed class GetEmailLoginNoticeRecipientCountEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/admin/campaigns/email-login-notice/recipient-count/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<CampaignRecipientCountResponse>> HandleAsync(
        [FromServices] IEmailLoginNoticeCampaignRunner runner,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.CountRecipientsAsync(ct);
        return result.Map(count => new CampaignRecipientCountResponse(count));
    }
}
