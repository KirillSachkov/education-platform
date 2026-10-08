using AccessService.Contracts.HomePins;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using ContentAccess;
using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.HomePins.UseCases;

/// <summary>
///     Закрепы для home-дашборда текущего пользователя — merge закрепов всех его активных
///     планов, дедуп по материалу, cap=12, enrich (ECS summaries) + per-item lock-state.
///     Status != Published материалы отфильтрованы. Soft-degrade: ECS down → пустой 200
///     (как GetPublicPlans), не 500. Epic #397.
/// </summary>
public sealed class GetMyHomePinsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/me/home-pins/",
                async Task<EndpointResult<IReadOnlyList<HomePinDto>>> (
                    [FromServices] GetMyHomePinsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetMyHomePinsQuery(), ct))
            .RequireAuthorization();
    }
}

public sealed record GetMyHomePinsQuery : IQuery;

public sealed class GetMyHomePinsHandler
    : IQueryHandlerWithResult<IReadOnlyList<HomePinDto>, GetMyHomePinsQuery>
{
    /// <summary>Максимум закрепов на дашборде — лишние дропаются с LogInformation.</summary>
    public const int MAX_PINS = 12;

    private const string PUBLISHED = "PUBLISHED";

    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IPlanPinnedMaterialRepository _pins;
    private readonly IEducationContentServiceClient _ecs;
    private readonly IEntitlementChecker _entitlements;
    private readonly UserScopedData _user;
    private readonly ILogger<GetMyHomePinsHandler> _logger;

    public GetMyHomePinsHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IPlanPinnedMaterialRepository pins,
        IEducationContentServiceClient ecs,
        IEntitlementChecker entitlements,
        UserScopedData user,
        ILogger<GetMyHomePinsHandler> logger)
    {
        _grants = grants;
        _plans = plans;
        _pins = pins;
        _ecs = ecs;
        _entitlements = entitlements;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<HomePinDto>, Error>> Handle(
        GetMyHomePinsQuery query,
        CancellationToken cancellationToken = default)
    {
        Guid userId = _user.UserId;

        // 1. Caller's ACTIVE grants → distinct plan ids (mirror GetMyAuthorContext).
        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            g => g.UserId == userId && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);

        if (activeGrants.Count == 0)
        {
            return Result.Success<IReadOnlyList<HomePinDto>, Error>([]);
        }

        Guid[] planIds = [.. activeGrants.Select(g => g.PlanId).Distinct()];

        // Tier per plan — нужен для порядка дедупа (tier desc, потом SortKey asc).
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => planIds.Contains(p.Id), cancellationToken);
        Dictionary<Guid, int> tierRankByPlan = plans.ToDictionary(p => p.Id, p => TierRank(p.Tier));

        // 2. Pins of those plans (ordered by (PlanId, SortKey asc)).
        IReadOnlyList<PlanPinnedMaterial> pins = await _pins.GetManyByPlansAsync(planIds, cancellationToken);
        if (pins.Count == 0)
        {
            return Result.Success<IReadOnlyList<HomePinDto>, Error>([]);
        }

        // 3. Dedup by MaterialId — keep the first under (tier desc, SortKey asc).
        List<PlanPinnedMaterial> deduped = pins
            .OrderByDescending(p => tierRankByPlan.TryGetValue(p.PlanId, out int r) ? r : 0)
            .ThenBy(p => p.SortKey.Value, StringComparer.Ordinal)
            .GroupBy(p => p.MaterialId)
            .Select(g => g.First())
            .ToList();

        // Stable presentation order: same ordering, dedup-collapsed.
        deduped = deduped
            .OrderByDescending(p => tierRankByPlan.TryGetValue(p.PlanId, out int r) ? r : 0)
            .ThenBy(p => p.SortKey.Value, StringComparer.Ordinal)
            .ToList();

        // 4. Cap at MAX_PINS — drop extras with a log (no silent truncation).
        if (deduped.Count > MAX_PINS)
        {
            _logger.LogInformation(
                "Home pins for user {UserId} exceeded cap: {Total} resolved, returning first {Cap}",
                userId, deduped.Count, MAX_PINS);
            deduped = deduped.Take(MAX_PINS).ToList();
        }

        // 5. Enrich via ECS summaries. Soft-degrade: ECS down → empty 200.
        Guid[] materialIds = deduped.Select(p => p.MaterialId).ToArray();
        Result<IReadOnlyList<MaterialSummaryDto>, Error> summariesResult =
            await _ecs.GetMaterialSummariesAsync(materialIds, cancellationToken);
        if (summariesResult.IsFailure)
        {
            _logger.LogWarning(
                "ECS GetMaterialSummaries failed for me/home-pins ({Error}); returning empty list",
                summariesResult.Error.GetMessage());
            return Result.Success<IReadOnlyList<HomePinDto>, Error>([]);
        }

        Dictionary<Guid, MaterialSummaryDto> summaryById = summariesResult.Value
            .Where(s => string.Equals(s.Status, PUBLISHED, StringComparison.Ordinal))
            .ToDictionary(s => s.Id);

        // 6. Per-item entitlement: batch Redis SINTER check.
        AccessSubject subject = _user.ToAccessSubject();
        Guid[] publishedIds = deduped
            .Where(p => summaryById.ContainsKey(p.MaterialId))
            .Select(p => p.MaterialId)
            .ToArray();

        IReadOnlyDictionary<Guid, AccessDecision> decisions = publishedIds.Length == 0
            ? new Dictionary<Guid, AccessDecision>()
            : await _entitlements.CheckAccessBatchAsync(
                subject, ResourceTypes.MATERIAL, publishedIds, cancellationToken);

        var result = new List<HomePinDto>(deduped.Count);
        foreach (PlanPinnedMaterial pin in deduped)
        {
            // Filter out non-published (and missing) materials.
            if (!summaryById.TryGetValue(pin.MaterialId, out MaterialSummaryDto? summary))
            {
                continue;
            }

            bool isAccessible = decisions.TryGetValue(pin.MaterialId, out AccessDecision? decision)
                                && decision.IsGranted;
            string? lockReason = isAccessible
                ? null
                : ResolveLockReason(summary.AccessType, subject.IsAuthenticated);

            result.Add(new HomePinDto(
                pin.MaterialId,
                summary.Title,
                summary.Kind,
                summary.ThumbnailUrl,
                pin.Note,
                isAccessible,
                lockReason,
                $"/knowledge-base/{pin.MaterialId}"));
        }

        return Result.Success<IReadOnlyList<HomePinDto>, Error>(result);
    }

    /// <summary>
    ///     Map material AccessType → lock reason для UI, выравнено с
    ///     <see cref="LockReasonResolver"/> семантикой. Анонимы → <c>anonymous</c>
    ///     (на практике не достигается — endpoint требует auth); ENROLLED без доступного
    ///     grant'а → <c>plan_required</c>; иначе → <c>not_enrolled</c>.
    /// </summary>
    private static string ResolveLockReason(string accessType, bool isAuthenticated)
    {
        if (!isAuthenticated)
        {
            return LockReasons.ANONYMOUS;
        }

        return string.Equals(accessType, "ENROLLED", StringComparison.Ordinal)
            ? LockReasons.PLAN_REQUIRED
            : LockReasons.NOT_ENROLLED;
    }

    private static int TierRank(PlanTier tier) => tier switch
    {
        PlanTier.FULL_ALL => 4,
        PlanTier.LEARN_ALL => 3,
        PlanTier.COURSE => 2,
        PlanTier.SUBSCRIPTION => 1,
        _ => 0,
    };
}
