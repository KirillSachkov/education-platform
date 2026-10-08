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
///     GET /access/me/access-status/ — состояние доступа для модалки «срок истёк» (#687).
///     В отличие от <see cref="GetMyGrantsEndpoint"/> (только ACTIVE), отдаёт ещё и недавно
///     истёкший grant, по которому пользователь потерял доступ — фронт показывает онбординг-
///     окно «ваш доступ закончился» с CTA на продление.
/// </summary>
public sealed class GetMyAccessStatusEndpoint : IEndpoint
{
    /// <summary>Окно «недавности» истёкшего доступа для показа модалки.</summary>
    public static readonly TimeSpan RecentlyExpiredWindow = TimeSpan.FromDays(30);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/me/access-status/", async Task<EndpointResult<AccessStatusDto>> (
                [FromServices] GetMyAccessStatusHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetMyAccessStatusQuery(), ct))
            .RequireAuthorization();
    }
}

public sealed record GetMyAccessStatusQuery : IQuery;

public sealed class GetMyAccessStatusHandler : IQueryHandlerWithResult<AccessStatusDto, GetMyAccessStatusQuery>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly UserScopedData _user;

    public GetMyAccessStatusHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        UserScopedData user)
    {
        _grants = grants;
        _plans = plans;
        _user = user;
    }

    public async Task<Result<AccessStatusDto, Error>> Handle(
        GetMyAccessStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        Guid userId = _user.UserId;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - GetMyAccessStatusEndpoint.RecentlyExpiredWindow;

        // Минимально необходимый набор (server-side фильтр, не вся история grant'ов юзера):
        // ACTIVE (для coverage-проверки) + EXPIRED за окно RecentlyExpiredWindow (кандидаты).
        // REVOKED и старые EXPIRED не грузим — endpoint дёргается на каждом маунте overlay'я.
        IReadOnlyList<PlanGrant> all = await _grants.GetManyByAsync(
            g => g.UserId == userId
                 && (g.Status == PlanGrantStatus.ACTIVE
                     || (g.Status == PlanGrantStatus.EXPIRED
                         && g.ExpiresAt != null
                         && g.ExpiresAt >= cutoff)),
            cancellationToken);

        List<PlanGrant> active = all.Where(g => g.Status == PlanGrantStatus.ACTIVE).ToList();
        bool hasActiveAccess = active.Count > 0;

        // Кандидаты: недавно истёкшие по TTL grant'ы, свежие сначала.
        List<PlanGrant> expiredRecent = all
            .Where(g => g.Status == PlanGrantStatus.EXPIRED
                        && g.ExpiresAt is not null
                        && g.ExpiresAt >= cutoff)
            .OrderByDescending(g => g.ExpiresAt)
            .ToList();

        if (expiredRecent.Count == 0)
        {
            return new AccessStatusDto(hasActiveAccess, RecentlyExpired: null);
        }

        // Планы и активных, и истёкших grant'ов — для scope-проверки.
        Guid[] planIds = active.Select(g => g.PlanId)
            .Concat(expiredRecent.Select(g => g.PlanId))
            .Distinct()
            .ToArray();
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(
            p => planIds.Contains(p.Id), cancellationToken);
        Dictionary<Guid, Plan> plansById = plans.ToDictionary(p => p.Id);

        // Первый истёкший grant, scope которого больше НЕ покрыт активным grant'ом — это и
        // есть реальная потеря доступа, о которой стоит сказать пользователю.
        foreach (PlanGrant expired in expiredRecent)
        {
            if (!plansById.TryGetValue(expired.PlanId, out Plan? expiredPlan))
            {
                continue;
            }

            if (GrantScopeGuard.IsAlreadyCovered(expiredPlan, active, plansById))
            {
                continue;
            }

            ExpiredAccessDto dto = new(
                expired.Id,
                expired.PlanId,
                expiredPlan.DisplayName.Value,
                expiredPlan.Tier.ToString(),
                expired.ExpiresAt!.Value);

            return new AccessStatusDto(hasActiveAccess, dto);
        }

        return new AccessStatusDto(hasActiveAccess, RecentlyExpired: null);
    }
}
