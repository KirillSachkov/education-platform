using AccessService.Contracts.Plans.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class GetMyPlansEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/", async Task<EndpointResult<IReadOnlyList<PlanDto>>> (
                [FromServices] GetMyPlansHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetMyPlansQuery(), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetMyPlansQuery : IQuery;

public sealed class GetMyPlansHandler : IQueryHandlerWithResult<IReadOnlyList<PlanDto>, GetMyPlansQuery>
{
    private readonly IPlansRepository _plans;
    private readonly UserScopedData _user;

    public GetMyPlansHandler(IPlansRepository plans, UserScopedData user)
    {
        _plans = plans;
        _user = user;
    }

    public async Task<Result<IReadOnlyList<PlanDto>, Error>> Handle(
        GetMyPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        bool canViewAllPlans = _user.IsAdmin;
        Guid userId = _user.UserId;

        // TRAINER-scope plans (the trainer subscription) are managed exclusively via /access/trainer-pro/*
        // — keep them out of the general author plan-management list too (#674), not just the public catalog.
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.Scope == PlanScope.PLATFORM && (canViewAllPlans || p.AuthorId == userId),
            cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<PlanDto> dtos = plans
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .Select(p => MapToDto(p, now))
            .ToList();
        return Result.Success<IReadOnlyList<PlanDto>, Error>(dtos);
    }

    internal static PlanDto MapToDto(Plan plan, DateTimeOffset now) => new(
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
        DiscountStartsAt: plan.DiscountStartsAt,
        DiscountEndsAt: plan.DiscountEndsAt,
        PromotionActive: plan.IsPromotionActive(now),
        EffectivePriceCents: plan.EffectivePriceCents(now),
        CourseIds: plan.GetCourseIds(),
        IncludesFutureContent: plan.IncludesFutureContent,
        TrialDurationDays: plan.TrialDurationDays,
        Capabilities: PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
        IsHighlighted: plan.IsHighlighted,
        TermKind: plan.Term.Kind.ToString(),
        TermRecurringDays: plan.Term.RecurringIntervalDays,
        IsPublic: plan.IsPublic,
        IsActive: plan.IsActive,
        DisplayOrder: plan.DisplayOrder,
        CreatedAt: plan.CreatedAt,
        ArchivedAt: plan.ArchivedAt,
        GithubOrgSlug: plan.GitHubOrg);
}
