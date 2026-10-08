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
/// <c>GET /notifications/admin/campaigns/level-test-invite/recipient-count</c> — размер адресуемой
/// аудитории кампании приглашений на тест уровня (#554): сумма всех id из keyset-пагинации
/// AuthService. Opt-out / выключенный Email-канал фильтруются на доставке, не здесь.
///
/// Auth — permission <c>Platform.ADMIN</c>.
/// </summary>
public sealed class GetLevelTestInviteRecipientCountEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/admin/campaigns/level-test-invite/recipient-count/", HandleAsync)
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    private static async Task<EndpointResult<CampaignRecipientCountResponse>> HandleAsync(
        [FromServices] ILevelTestInviteCampaignRunner runner,
        CancellationToken ct)
    {
        Result<int, Error> result = await runner.CountRecipientsAsync(ct);
        return result.Map(count => new CampaignRecipientCountResponse(count));
    }
}
