using Core.HttpCommunication;

namespace EducationContentService.Core.Features.Plans;

internal sealed class CoursePricingHttpClient : BaseHttpClient, ICoursePricingClient
{
    private const string SERVICE_NAME = "AccessService";

    public CoursePricingHttpClient(
        HttpClient httpClient,
        ILogger<CoursePricingHttpClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>> GetPlansForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken ct)
    {
        if (courseIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                new Dictionary<Guid, CoursePricingDto>());
        }

        GetPlansByCourseIdsRequest body = new(courseIds.Distinct().ToArray());

        Result<GetPlansByCourseIdsResponse, Error> result =
            await PostAsync<GetPlansByCourseIdsRequest, GetPlansByCourseIdsResponse>(
                "/internal/access/plans/by-course-ids",
                body,
                ct);

        if (result.IsFailure)
            return result.Error;

        // A course can now be covered by MORE THAN ONE active COURSE plan (#404 bundles:
        // its own single-course plan AND any bundle that lists it). The catalog card shows
        // a single "от N ₽" hint, so we keep the CHEAPEST plan per course — min by list
        // PriceCents (tie-broken by PlanId for determinism). We compare on list price, not
        // effective: the cache is intentionally time-independent (raw promo window cached,
        // effective computed read-time in GetCatalog), and a "from" hint being at most a
        // promo-depth off the absolute cheapest is acceptable for a teaser.
        Dictionary<Guid, CoursePricingDto> map = [];
        foreach (PlanByCourseDto plan in result.Value.Plans)
        {
            // Plans without a price (PriceCents == null) are excluded — the cache only
            // tracks priceable course plans. Callers treat absence as "no price".
            if (plan.PriceCents is not { } priceCents)
                continue;

            if (map.TryGetValue(plan.CourseId, out CoursePricingDto? existing)
                && !IsCheaper(priceCents, plan.PlanId, existing))
            {
                continue;
            }

            map[plan.CourseId] = new CoursePricingDto(
                PlanId: plan.PlanId,
                AuthorId: plan.AuthorId,
                CourseId: plan.CourseId,
                Slug: plan.Slug,
                DisplayName: plan.DisplayName,
                PriceCents: priceCents,
                Currency: plan.Currency,
                DiscountPercent: plan.DiscountPercent,
                DiscountStartsAt: plan.DiscountStartsAt,
                DiscountEndsAt: plan.DiscountEndsAt);
        }

        return Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(map);
    }

    // Is the candidate (price, planId) strictly cheaper than the one already mapped for
    // the course? Lower list price wins; ties broken by the smaller PlanId so selection is
    // deterministic regardless of AccessService row order.
    private static bool IsCheaper(long candidatePrice, Guid candidatePlanId, CoursePricingDto existing) =>
        candidatePrice < existing.PriceCents
        || (candidatePrice == existing.PriceCents && candidatePlanId.CompareTo(existing.PlanId) < 0);

    private sealed record GetPlansByCourseIdsRequest(IReadOnlyList<Guid> CourseIds);

    private sealed record GetPlansByCourseIdsResponse(IReadOnlyList<PlanByCourseDto> Plans);

    private sealed record PlanByCourseDto(
        Guid PlanId,
        Guid CourseId,
        Guid AuthorId,
        string Tier,
        string Slug,
        string DisplayName,
        long? PriceCents,
        string Currency,
        bool IsActive,
        bool IsPublic,
        int? DiscountPercent,
        DateTimeOffset? DiscountStartsAt,
        DateTimeOffset? DiscountEndsAt);
}
