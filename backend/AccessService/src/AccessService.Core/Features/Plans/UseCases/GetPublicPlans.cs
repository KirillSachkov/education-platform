using AccessService.Contracts.Plans.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class GetPublicPlansEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/public", async Task<EndpointResult<IReadOnlyList<PublicPlanDto>>> (
                [FromServices] GetPublicPlansHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetPublicPlansQuery(), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed record GetPublicPlansQuery : IQuery;

public sealed class GetPublicPlansHandler
    : IQueryHandlerWithResult<IReadOnlyList<PublicPlanDto>, GetPublicPlansQuery>
{
    private readonly IPlansRepository _plans;
    private readonly IEducationContentServiceClient _eduClient;
    private readonly ILogger<GetPublicPlansHandler> _logger;

    public GetPublicPlansHandler(
        IPlansRepository plans,
        IEducationContentServiceClient eduClient,
        ILogger<GetPublicPlansHandler> logger)
    {
        _plans = plans;
        _eduClient = eduClient;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PublicPlanDto>, Error>> Handle(
        GetPublicPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        // Historical retired offers stay isolated from the platform catalog.
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.IsPublic && p.IsActive && p.Scope == PlanScope.PLATFORM,
            cancellationToken);

        IReadOnlyList<Plan> ordered = plans
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .ToList();

        IReadOnlyDictionary<Guid, CourseTitleDto> courseTitles = await ResolveCourseTitlesAsync(ordered, cancellationToken);

        // Один снимок времени на весь батч — все планы в ответе оценивают акцию
        // относительно одного `now`.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<PublicPlanDto> dtos = ordered
            .Select(p => MapToPublicDto(p, courseTitles, now))
            .ToList();

        return Result.Success<IReadOnlyList<PublicPlanDto>, Error>(dtos);
    }

    /// <summary>
    /// Batch-resolve course titles for COURSES plans in a single ECS call.
    /// Soft-degrade: ECS down → empty dict → plans rendered without "Включает курсы".
    /// Pricing-страница не должна падать из-за content-service downtime.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, CourseTitleDto>> ResolveCourseTitlesAsync(
        IReadOnlyList<Plan> plans,
        CancellationToken ct)
    {
        Guid[] courseIds = plans
            .Where(p => p.Tier == PlanTier.COURSE)
            .SelectMany(p => p.GetCourseIds())
            .Distinct()
            .ToArray();

        if (courseIds.Length == 0)
        {
            return new Dictionary<Guid, CourseTitleDto>();
        }

        Result<IReadOnlyList<CourseTitleDto>, Error> lookup =
            await _eduClient.GetCourseTitlesAsync(courseIds, ct);

        if (lookup.IsFailure)
        {
            _logger.LogWarning(
                "ECS course-titles lookup failed for {Count} ids: {Code}",
                courseIds.Length, lookup.Error.Messages[0].Code);
            return new Dictionary<Guid, CourseTitleDto>();
        }

        return lookup.Value.ToDictionary(c => c.CourseId);
    }

    internal static PublicPlanDto MapToPublicDto(Plan plan, DateTimeOffset now) =>
        MapToPublicDto(plan, new Dictionary<Guid, CourseTitleDto>(), now);

    internal static PublicPlanDto MapToPublicDto(
        Plan plan,
        IReadOnlyDictionary<Guid, CourseTitleDto> courseTitles,
        DateTimeOffset now)
    {
        IReadOnlyList<Guid> bundleCourseIds = plan.GetCourseIds();

        IReadOnlyList<PublicPlanCourseDto>? included = null;
        if (plan.Tier == PlanTier.COURSE && bundleCourseIds.Count > 0)
        {
            // Resolve every bundle course we have a title for; soft-degrade drops unresolved ids.
            List<PublicPlanCourseDto> resolved = [];
            foreach (Guid courseId in bundleCourseIds)
            {
                if (courseTitles.TryGetValue(courseId, out CourseTitleDto? course))
                {
                    resolved.Add(new PublicPlanCourseDto(courseId, course.Title, course.Slug, course.Kind));
                }
            }

            included = resolved.Count > 0 ? resolved : null;
        }

        // Эффективная цена считается на момент `now` (один снимок на запрос) —
        // после окончания окна акции план сам отдаётся по обычной цене, без джоба.
        return new PublicPlanDto(
            Id: plan.Id,
            AuthorId: plan.AuthorId,
            Tier: plan.Tier.ToString(),
            OfferType: plan.OfferType.ToString(),
            Slug: plan.Slug.Value,
            DisplayName: plan.DisplayName.Value,
            ShortDescription: plan.ShortDescription,
            LongDescription: plan.LongDescription,
            CoverFileId: plan.CoverFileId,
            Features: plan.Features,
            PriceCents: plan.PriceCents,
            Currency: plan.Currency,
            DiscountPercent: plan.DiscountPercent,
            DiscountEndsAt: plan.DiscountEndsAt,
            PromotionActive: plan.IsPromotionActive(now),
            EffectivePriceCents: plan.EffectivePriceCents(now),
            CourseId: bundleCourseIds.Count > 0 ? bundleCourseIds[0] : null,
            CourseIds: bundleCourseIds,
            IncludesFutureContent: plan.IncludesFutureContent,
            TrialDurationDays: plan.TrialDurationDays,
            Capabilities: PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
            IsHighlighted: plan.IsHighlighted,
            TermKind: plan.Term.Kind.ToString(),
            TermRecurringDays: plan.Term.RecurringIntervalDays,
            DisplayOrder: plan.DisplayOrder,
            CreatedAt: plan.CreatedAt,
            IncludedCourses: included);
    }
}