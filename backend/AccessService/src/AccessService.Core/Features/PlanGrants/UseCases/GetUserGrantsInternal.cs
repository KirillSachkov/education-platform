using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// Service-to-service endpoint used by ProgressService for Redis grant resync.
/// Locked down by role rather than nginx routing — nginx exposes only <c>/api/*</c>,
/// but in-cluster callers go through it via direct service hostname.
/// </summary>
public sealed class GetUserGrantsInternalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/access/users/{userId:guid}/grants", async Task<EndpointResult<IReadOnlyList<PlanGrantDto>>> (
                [FromRoute] Guid userId,
                [FromServices] GetUserGrantsInternalHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetUserGrantsInternalQuery(userId), ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetUserGrantsInternalQuery(Guid UserId) : IQuery;

public sealed class GetUserGrantsInternalHandler
    : IQueryHandlerWithResult<IReadOnlyList<PlanGrantDto>, GetUserGrantsInternalQuery>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;

    public GetUserGrantsInternalHandler(IPlanGrantsRepository grants, IPlansRepository plans)
    {
        _grants = grants;
        _plans = plans;
    }

    public async Task<Result<IReadOnlyList<PlanGrantDto>, Error>> Handle(
        GetUserGrantsInternalQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlanGrant> grants = await _grants.GetManyByAsync(
            g => g.UserId == query.UserId && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);

        Guid[] planIds = grants.Select(grant => grant.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> planRows = await _plans.GetManyByAsync(
            plan => planIds.Contains(plan.Id),
            cancellationToken);
        Dictionary<Guid, Plan> planById = planRows.ToDictionary(plan => plan.Id);

        Guid[] trialAuthorIds = planRows
            .Where(plan => plan.IsTrial)
            .Select(plan => plan.AuthorId)
            .Distinct()
            .ToArray();
        IReadOnlyList<Plan> canonicalCandidates = trialAuthorIds.Length == 0
            ? []
            : await _plans.GetManyByAsync(
                plan => trialAuthorIds.Contains(plan.AuthorId)
                        && plan.Tier == PlanTier.FULL_ALL
                        && plan.TrialDurationDays == null
                        && plan.ArchivedAt == null
                        && plan.IsActive
                        && plan.IsPublic,
                cancellationToken);
        Dictionary<Guid, Guid> canonicalByAuthorId = canonicalCandidates
            .GroupBy(plan => plan.AuthorId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(plan => plan.IsHighlighted)
                    .ThenBy(plan => plan.DisplayOrder)
                    .ThenBy(plan => plan.CreatedAt)
                    .ThenBy(plan => plan.Id)
                    .First()
                    .Id);

        IReadOnlyList<PlanGrantDto> dtos = grants
            .Select(grant =>
            {
                planById.TryGetValue(grant.PlanId, out Plan? plan);
                Guid? telegramBindingPlanId = plan switch
                {
                    null => null,
                    { IsTrial: true } when canonicalByAuthorId.TryGetValue(plan.AuthorId, out Guid canonicalId)
                        => canonicalId,
                    { IsTrial: true } => null,
                    _ => plan.Id,
                };
                return PlanGrantMapper.MapToDtoWithTelegramAccess(
                    grant,
                    plan,
                    telegramBindingPlanId);
            })
            .ToList();
        return Result.Success<IReadOnlyList<PlanGrantDto>, Error>(dtos);
    }
}
