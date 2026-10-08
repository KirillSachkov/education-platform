using AccessService.Contracts.TrainerPro;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.TrainerPro.UseCases;

/// <summary>
/// <c>GET /access/admin/trainer-pro/offer</c> — автор-вид всех (опубликованных и черновых)
/// оффер-вариантов тренажёра (#674) для страницы управления оффером на <c>/trainer/admin</c>.
/// Возвращает Scope=TRAINER планы без архивных, с флагами <c>isPublic/isActive</c>. Сидит рядом с
/// существующим <c>GET /access/admin/trainer-pro/revenue</c>; та же auth-политика
/// (<see cref="PlatformPermissions.Plans.MANAGE"/>, платформа одно-авторская — без per-plan ownership).
/// </summary>
public sealed class GetAdminTrainerProOfferEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/admin/trainer-pro/offer", async Task<EndpointResult<IReadOnlyList<TrainerProOfferAdminDto>>> (
                [FromServices] GetAdminTrainerProOfferHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetAdminTrainerProOfferQuery(), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetAdminTrainerProOfferQuery : IQuery;

public sealed class GetAdminTrainerProOfferHandler
    : IQueryHandlerWithResult<IReadOnlyList<TrainerProOfferAdminDto>, GetAdminTrainerProOfferQuery>
{
    private readonly IPlansRepository _plans;

    public GetAdminTrainerProOfferHandler(IPlansRepository plans) => _plans = plans;

    public async Task<Result<IReadOnlyList<TrainerProOfferAdminDto>, Error>> Handle(
        GetAdminTrainerProOfferQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.Scope == PlanScope.TRAINER && p.ArchivedAt == null,
            cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<TrainerProOfferAdminDto> dtos = plans
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .Select(p => MapToAdminDto(p, now))
            .ToList();

        return Result.Success<IReadOnlyList<TrainerProOfferAdminDto>, Error>(dtos);
    }

    internal static TrainerProOfferAdminDto MapToAdminDto(Plan plan, DateTimeOffset now) => new(
        Id: plan.Id,
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
        RecurringIntervalDays: plan.Term.RecurringIntervalDays,
        Capabilities: PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
        IsHighlighted: plan.IsHighlighted,
        IsPublic: plan.IsPublic,
        IsActive: plan.IsActive,
        DisplayOrder: plan.DisplayOrder,
        CreatedAt: plan.CreatedAt,
        ArchivedAt: plan.ArchivedAt);
}
