using AccessService.Contracts.Plans.Dtos;
using AccessService.Core.Database;
using AccessService.Core.Features.PlanGrants;
using AccessService.Core.Features.Plans;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Plans.UseCases;

/// <summary>
/// Service-to-service: Telegram-инфо плана (настроенное приветствие + display name).
/// Используется TelegramBotService для поста приветствия в группу при входе участника
/// и для именования оффера в claim-сообщении. Locked down by role (nginx exposes only /api/*).
/// </summary>
public sealed class GetPlanTelegramInfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/access/plans/{planId:guid}/telegram-info", async Task<EndpointResult<PlanTelegramInfoDto>> (
                [FromRoute] Guid planId,
                [FromServices] GetPlanTelegramInfoHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetPlanTelegramInfoQuery(planId), ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetPlanTelegramInfoQuery(Guid PlanId) : IQuery;

public sealed class GetPlanTelegramInfoHandler
    : IQueryHandlerWithResult<PlanTelegramInfoDto, GetPlanTelegramInfoQuery>
{
    private readonly IPlansRepository _plans;

    public GetPlanTelegramInfoHandler(IPlansRepository plans) => _plans = plans;

    public async Task<Result<PlanTelegramInfoDto, Error>> Handle(
        GetPlanTelegramInfoQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> get = await _plans.GetByAsync(p => p.Id == query.PlanId, cancellationToken);
        if (get.IsFailure)
        {
            return get.Error;
        }

        Plan plan = get.Value;
        Guid? canonicalTelegramPlanId = await CanonicalTelegramPlanResolver.ResolveAsync(
            plan,
            _plans,
            cancellationToken);

        return new PlanTelegramInfoDto(
            plan.Id,
            plan.DisplayName.Value,
            plan.Tier.ToString(),
            plan.TelegramWelcomeMessage,
            canonicalTelegramPlanId,
            PlanCapabilitiesMapper.ToStrings(plan.Capabilities));
    }

}
