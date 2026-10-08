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
/// <c>GET /access/trainer-pro/offer</c> — публичный (анонимный) список покупаемых вариантов подписки
/// тренажёра (#674): активные опубликованные планы Scope=TRAINER (период-варианты), цена, id'ы.
/// Зеркалит auth-политику платформенного каталога (<c>GetPublicPlans</c> — anonymous read).
/// </summary>
public sealed class GetTrainerProOfferEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/trainer-pro/offer", async Task<EndpointResult<IReadOnlyList<TrainerProOfferDto>>> (
                [FromServices] GetTrainerProOfferHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetTrainerProOfferQuery(), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed record GetTrainerProOfferQuery : IQuery;

public sealed class GetTrainerProOfferHandler
    : IQueryHandlerWithResult<IReadOnlyList<TrainerProOfferDto>, GetTrainerProOfferQuery>
{
    private readonly IPlansRepository _plans;

    public GetTrainerProOfferHandler(IPlansRepository plans) => _plans = plans;

    public async Task<Result<IReadOnlyList<TrainerProOfferDto>, Error>> Handle(
        GetTrainerProOfferQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => p.Scope == PlanScope.TRAINER
                 && p.IsPublic
                 && p.IsActive
                 && p.ArchivedAt == null,
            cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<TrainerProOfferDto> dtos = plans
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .Select(p => MapToDto(p, now))
            .ToList();

        return Result.Success<IReadOnlyList<TrainerProOfferDto>, Error>(dtos);
    }

    internal static TrainerProOfferDto MapToDto(Plan plan, DateTimeOffset now) => new(
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
        DiscountEndsAt: plan.DiscountEndsAt,
        PromotionActive: plan.IsPromotionActive(now),
        EffectivePriceCents: plan.EffectivePriceCents(now),
        RecurringIntervalDays: plan.Term.RecurringIntervalDays,
        Capabilities: PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
        IsHighlighted: plan.IsHighlighted,
        DisplayOrder: plan.DisplayOrder);
}
