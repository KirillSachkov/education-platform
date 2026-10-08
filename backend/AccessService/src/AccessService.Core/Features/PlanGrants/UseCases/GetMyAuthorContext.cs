using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// Legacy route for the current user's plan context. Access is platform/course
/// scoped now: FULL_ALL / LEARN_ALL apply globally, COURSE/SUBSCRIPTION are
/// evaluated by courseIds on the client. The authorId route parameter is kept
/// for backwards-compatible frontend call-sites. Issue #83/#599/#608.
/// </summary>
public sealed class GetMyAuthorContextEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/me/author-context/{authorId:guid}",
                async Task<EndpointResult<AuthorContextDto>> (
                    [FromRoute] Guid authorId,
                    [FromServices] GetMyAuthorContextHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetMyAuthorContextQuery(authorId), ct))
            .RequireAuthorization();
    }
}

public sealed record GetMyAuthorContextQuery(Guid AuthorId) : IQuery;

public sealed class GetMyAuthorContextHandler
    : IQueryHandlerWithResult<AuthorContextDto, GetMyAuthorContextQuery>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly UserScopedData _user;

    public GetMyAuthorContextHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        UserScopedData user)
    {
        _grants = grants;
        _plans = plans;
        _user = user;
    }

    public async Task<Result<AuthorContextDto, Error>> Handle(
        GetMyAuthorContextQuery query,
        CancellationToken cancellationToken = default)
    {
        Guid userId = _user.UserId;

        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            g => g.UserId == userId && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);

        if (activeGrants.Count == 0)
            return new AuthorContextDto(false, "registered", []);

        Guid[] planIds = [.. activeGrants.Select(g => g.PlanId).Distinct()];
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => planIds.Contains(p.Id),
            cancellationToken);

        if (plans.Count == 0)
            return new AuthorContextDto(false, "registered", []);

        Dictionary<Guid, Plan> planById = plans.ToDictionary(p => p.Id);
        List<PlanGrant> authorGrants = [.. activeGrants.Where(g => planById.ContainsKey(g.PlanId))];

        if (authorGrants.Count == 0)
            return new AuthorContextDto(false, "registered", []);

        string highestTier = ResolveHighestTier(authorGrants, planById);

        // HasOnboardingEnabled намеренно false здесь — author-context показывает
        // план в SpaceSidebar, не настройки. Restart-onboarding-кнопка для этого
        // потока не нужна; передавать flow-словарь смысла нет.
        IReadOnlyList<PlanGrantDto> dtos = [.. authorGrants
            .Select(g => PlanGrantMapper.MapToDtoWithPlan(g, planById[g.PlanId]))];

        return new AuthorContextDto(true, highestTier, dtos);
    }

    private static string ResolveHighestTier(
        IReadOnlyList<PlanGrant> grants,
        IReadOnlyDictionary<Guid, Plan> plans)
    {
        bool hasFull = grants.Any(g =>
            plans.TryGetValue(g.PlanId, out Plan? p) && p.Tier == PlanTier.FULL_ALL);
        if (hasFull) return "full_all";

        bool hasLearn = grants.Any(g =>
            plans.TryGetValue(g.PlanId, out Plan? p) && p.Tier == PlanTier.LEARN_ALL);
        if (hasLearn) return "learn_all";

        bool hasCourse = grants.Any(g =>
            plans.TryGetValue(g.PlanId, out Plan? p)
            && (p.Tier == PlanTier.COURSE || p.Tier == PlanTier.SUBSCRIPTION));
        if (hasCourse) return "course";

        // FREE-tier grants legacy-only (#358) — у пользователя с одним только FREE-grant
        // эффективный доступ = "registered" (бесплатный = system default).
        return "registered";
    }
}
