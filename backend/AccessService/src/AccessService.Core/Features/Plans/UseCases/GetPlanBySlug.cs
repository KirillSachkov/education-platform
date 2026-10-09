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

public sealed class GetPlanBySlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/by-slug/{slug}", async Task<EndpointResult<PublicPlanDto>> (
                [FromRoute] string slug,
                [FromServices] GetPlanBySlugHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetPlanBySlugQuery(slug), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed record GetPlanBySlugQuery(string Slug) : IQuery;

public sealed class GetPlanBySlugHandler
    : IQueryHandlerWithResult<PublicPlanDto, GetPlanBySlugQuery>
{
    private readonly IPlansRepository _plans;
    private readonly IEducationContentServiceClient _eduClient;
    private readonly ILogger<GetPlanBySlugHandler> _logger;

    public GetPlanBySlugHandler(
        IPlansRepository plans,
        IEducationContentServiceClient eduClient,
        ILogger<GetPlanBySlugHandler> logger)
    {
        _plans = plans;
        _eduClient = eduClient;
        _logger = logger;
    }

    public async Task<Result<PublicPlanDto, Error>> Handle(
        GetPlanBySlugQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<PlanSlug, Error> slugResult = PlanSlug.Of(query.Slug);
        if (slugResult.IsFailure)
        {
            return AccessErrors.PlanNotFound();
        }

        // EF gotcha: owned-VO equality doesn't translate; pull primitive into local.
        string slugValue = slugResult.Value.Value;

        // Historical retired offers stay isolated from the platform catalog.
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.Slug.Value == slugValue
                 && p.IsPublic
                 && p.IsActive
                 && p.Scope == PlanScope.PLATFORM,
            cancellationToken);

        Plan? plan = plans
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefault();

        if (plan is null)
        {
            return AccessErrors.PlanNotFound();
        }

        IReadOnlyDictionary<Guid, CourseTitleDto> titles = await ResolveTitlesAsync(plan, cancellationToken);
        return GetPublicPlansHandler.MapToPublicDto(plan, titles, DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyDictionary<Guid, CourseTitleDto>> ResolveTitlesAsync(
        Plan plan,
        CancellationToken ct)
    {
        if (plan.Tier != PlanTier.COURSE || plan.GetCourseIds().Count == 0)
        {
            return new Dictionary<Guid, CourseTitleDto>();
        }

        Result<IReadOnlyList<CourseTitleDto>, Error> lookup =
            await _eduClient.GetCourseTitlesAsync([.. plan.GetCourseIds()], ct);

        if (lookup.IsFailure)
        {
            _logger.LogWarning(
                "ECS course-titles lookup failed for plan {PlanId}: {Code}",
                plan.Id, lookup.Error.Messages[0].Code);
            return new Dictionary<Guid, CourseTitleDto>();
        }

        return lookup.Value.ToDictionary(c => c.CourseId);
    }
}