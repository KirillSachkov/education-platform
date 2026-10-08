using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
///     <c>GET /access/plans/{planId}/upgrade-quote/</c>. Phase 2 #112.
///
///     Auth required (anonymous пользователю показываем full price без credit'а —
///     credit рассчитывается только для залогиненных). Возвращает <c>UpgradeQuoteDto</c>:
///     final price + breakdown сумм credit'а из существующих grants.
///
///     Используется фронтом на pricing-странице автора: при mount'е каждой PlanCard
///     auth'нутый юзер вызывает этот endpoint и видит индивидуальную цену.
/// </summary>
public sealed class GetUpgradeQuoteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/upgrade-quote/",
                async Task<EndpointResult<UpgradeQuoteDto>> (
                    [FromRoute] Guid planId,
                    [FromServices] GetUpgradeQuoteHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetUpgradeQuoteQuery(planId), ct))
            .RequireAuthorization()
            .RequireRateLimiting("upgrade-quote");
    }
}

public sealed record GetUpgradeQuoteQuery(Guid PlanId) : IQuery;

public sealed class GetUpgradeQuoteHandler
    : IQueryHandlerWithResult<UpgradeQuoteDto, GetUpgradeQuoteQuery>
{
    private readonly IPlansRepository _plans;
    private readonly IUpgradeCreditCalculator _calculator;
    private readonly UserScopedData _user;

    public GetUpgradeQuoteHandler(
        IPlansRepository plans,
        IUpgradeCreditCalculator calculator,
        UserScopedData user)
    {
        _plans = plans;
        _calculator = calculator;
        _user = user;
    }

    public async Task<Result<UpgradeQuoteDto, Error>> Handle(
        GetUpgradeQuoteQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == query.PlanId,
            cancellationToken);
        if (getPlan.IsFailure) return getPlan.Error;

        Plan plan = getPlan.Value;

        // План должен быть active+public — иначе anonymous-friendly «not found», без
        // утечки draft/архивных планов.
        if (!plan.IsActive || !plan.IsPublic || plan.ArchivedAt is not null)
        {
            return AccessErrors.PlanNotFound();
        }

        UpgradeQuote quote = await _calculator.CalculateAsync(
            _user.UserId,
            plan,
            cancellationToken);

        return new UpgradeQuoteDto(
            quote.OriginalPriceCents,
            quote.CreditCents,
            quote.FinalPriceCents,
            quote.IsOwned,
            [.. quote.Sources.Select(s => new UpgradeCreditSourceDto(
                s.GrantId,
                s.PlanId,
                s.PlanDisplayName,
                s.PlanTier.ToString(),
                s.CreditCents))]);
    }
}
