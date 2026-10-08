using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Plans.UseCases;

/// <summary>
/// Service-to-service batch lookup of active+public+non-archived COURSE-tier plans
/// by course IDs. Used by EducationContentService to enrich course catalog DTOs
/// with `priceCents` / `currency` / `planSlug` (course price model).
/// </summary>
public sealed class GetPlansByCourseIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/access/plans/by-course-ids", async Task<EndpointResult<GetPlansByCourseIdsResponse>> (
                [FromBody] GetPlansByCourseIdsRequest request,
                [FromServices] GetPlansByCourseIdsHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetPlansByCourseIdsQuery(request.CourseIds), ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetPlansByCourseIdsRequest(IReadOnlyList<Guid> CourseIds);

public sealed record PlanByCourseDto(
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
    // Raw акция-поля (окно дат + процент). ECS кеширует их как есть и считает
    // эффективную цену / активность в read-time с `now` — кеш не зависит от времени,
    // инвалидируется только при изменении акции автором (через plan_course.bound).
    int? DiscountPercent,
    DateTimeOffset? DiscountStartsAt,
    DateTimeOffset? DiscountEndsAt);

public sealed record GetPlansByCourseIdsResponse(IReadOnlyList<PlanByCourseDto> Plans);

public sealed record GetPlansByCourseIdsQuery(IReadOnlyList<Guid> CourseIds) : IQuery;

public sealed class GetPlansByCourseIdsHandler
    : IQueryHandlerWithResult<GetPlansByCourseIdsResponse, GetPlansByCourseIdsQuery>
{
    private const int MAX_IDS = 200;

    private readonly IPlansRepository _plans;

    public GetPlansByCourseIdsHandler(IPlansRepository plans) => _plans = plans;

    public async Task<Result<GetPlansByCourseIdsResponse, Error>> Handle(
        GetPlansByCourseIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.CourseIds.Count == 0)
        {
            return new GetPlansByCourseIdsResponse([]);
        }

        if (query.CourseIds.Count > MAX_IDS)
        {
            return Error.Validation(
                "plans.by_course.too_many",
                $"Не больше {MAX_IDS} идентификаторов курса за один запрос.");
        }

        // De-duplicate server-side; the bundle join tolerates duplicates anyway but the
        // shorter set keeps the membership check compact.
        HashSet<Guid> ids = [.. query.CourseIds];

        // Bundle (#404): a COURSE plan now lists N courses via Courses. Load active+public
        // plans that bind at least one of the requested courses, then emit ONE row per
        // (plan, matched course) — keeps the per-course DTO shape ECS consumes unchanged.
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.Tier == PlanTier.COURSE
                && p.Scope == PlanScope.PLATFORM // #674 defense-in-depth — TRAINER offers are SUBSCRIPTION-tier anyway
                && p.IsActive
                && p.IsPublic
                && p.ArchivedAt == null
                && p.Courses.Any(c => ids.Contains(c.CourseId)),
            cancellationToken);

        List<PlanByCourseDto> dtos = [];
        foreach (Plan p in plans)
        {
            foreach (PlanCourse pc in p.Courses)
            {
                Guid courseId = pc.CourseId;
                if (!ids.Contains(courseId))
                {
                    continue;
                }

                dtos.Add(new PlanByCourseDto(
                    PlanId: p.Id,
                    CourseId: courseId,
                    AuthorId: p.AuthorId,
                    Tier: p.Tier.ToString(),
                    Slug: p.Slug.Value,
                    DisplayName: p.DisplayName.Value,
                    PriceCents: p.PriceCents,
                    Currency: p.Currency,
                    IsActive: p.IsActive,
                    IsPublic: p.IsPublic,
                    DiscountPercent: p.DiscountPercent,
                    DiscountStartsAt: p.DiscountStartsAt,
                    DiscountEndsAt: p.DiscountEndsAt));
            }
        }

        return new GetPlansByCourseIdsResponse(dtos);
    }
}
