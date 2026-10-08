using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class GetMyGrantsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/me/grants/", async Task<EndpointResult<IReadOnlyList<PlanGrantDto>>> (
                [FromServices] GetMyGrantsHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetMyGrantsQuery(), ct))
            .RequireAuthorization();
    }
}

public sealed record GetMyGrantsQuery : IQuery;

public sealed class GetMyGrantsHandler : IQueryHandlerWithResult<IReadOnlyList<PlanGrantDto>, GetMyGrantsQuery>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly UserScopedData _user;

    public GetMyGrantsHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        UserScopedData user)
    {
        _grants = grants;
        _plans = plans;
        _flows = flows;
        _user = user;
    }

    public async Task<Result<IReadOnlyList<PlanGrantDto>, Error>> Handle(
        GetMyGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        Guid userId = _user.UserId;

        IReadOnlyList<PlanGrant> grants = await _grants.GetManyByAsync(
            g => g.UserId == userId && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);

        if (grants.Count == 0)
        {
            return Result.Success<IReadOnlyList<PlanGrantDto>, Error>([]);
        }

        // Batch-load plans referenced by the user's grants — populates the embedded
        // `plan` field so /settings/plans renders capabilities/kind in one round-trip.
        Guid[] planIds = grants.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => planIds.Contains(p.Id), cancellationToken);
        Dictionary<Guid, Plan> planById = plans.ToDictionary(p => p.Id);

        // Batch-load onboarding flows — нужно знать у каких planов настроен flow
        // (IsEnabled), чтобы фронт скрывал «Пройти онбординг заново» у планов
        // без онбординга.
        IReadOnlyList<PlanOnboardingFlow> flows = await _flows.GetManyByAsync(
            f => planIds.Contains(f.PlanId), cancellationToken);
        Dictionary<Guid, bool> onboardingEnabledByPlanId = flows.ToDictionary(
            f => f.PlanId, f => f.IsEnabled);

        IReadOnlyList<PlanGrantDto> dtos = grants
            .Select(g =>
            {
                planById.TryGetValue(g.PlanId, out Plan? plan);
                return PlanGrantMapper.MapToDtoWithPlan(g, plan, onboardingEnabledByPlanId);
            })
            .ToList();
        return Result.Success<IReadOnlyList<PlanGrantDto>, Error>(dtos);
    }
}
