using System.Globalization;
using System.Text;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class ListPlanGrantsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/grants/", async Task<EndpointResult<PlanGrantsPageDto>> (
                [FromRoute] Guid planId,
                [FromQuery] string? cursor,
                [FromQuery] int? limit,
                [FromQuery] string? search,
                [FromServices] ListPlanGrantsHandler handler,
                CancellationToken ct) =>
                await handler.Handle(
                    new ListPlanGrantsQuery(planId, cursor, limit, search),
                    ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ListPlanGrantsQuery(
    Guid PlanId,
    string? Cursor,
    int? Limit,
    string? Search) : IQuery;

public sealed class ListPlanGrantsHandler : IQueryHandlerWithResult<PlanGrantsPageDto, ListPlanGrantsQuery>
{
    private const int DEFAULT_LIMIT = 20;
    private const int MAX_LIMIT = 100;
    private const int SEARCH_USER_LOOKUP_LIMIT = 50;

    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IAuthServiceClient _authClient;
    private readonly UserScopedData _user;
    private readonly ILogger<ListPlanGrantsHandler> _logger;

    public ListPlanGrantsHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IAuthServiceClient authClient,
        UserScopedData user,
        ILogger<ListPlanGrantsHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _authClient = authClient;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<PlanGrantsPageDto, Error>> Handle(
        ListPlanGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == query.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;
        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        int limit = NormalizeLimit(query.Limit);

        // Search-mode: AuthService user-search → user IDs → narrow keyset query.
        // Soft-degrade: AuthService down → empty page (better than 500).
        IReadOnlyList<Guid>? userIdsFilter = null;
        string? trimmedSearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (trimmedSearch is not null)
        {
            Result<IReadOnlyList<AuthUserLookupDto>, Error> searchResult =
                await _authClient.SearchUsersAsync(trimmedSearch, SEARCH_USER_LOOKUP_LIMIT, cancellationToken);

            if (searchResult.IsFailure)
            {
                // LogError, не Warning: silent degradation для author-side ищет —
                // если AuthService недоступен, страница пустая и автор не знает почему.
                // Хотим чтобы on-call team увидела это в alerts.
                _logger.LogError(
                    "ListPlanGrants user-search failed: {ErrorMessage}",
                    searchResult.Error.GetMessage());
                return new PlanGrantsPageDto([], null);
            }

            userIdsFilter = searchResult.Value.Select(u => u.UserId).ToList();
            if (userIdsFilter.Count == 0)
            {
                return new PlanGrantsPageDto([], null);
            }
        }

        (DateTimeOffset? cursorGrantedAt, Guid? cursorId) = DecodeCursor(query.Cursor);

        // Берём limit+1 чтобы понять есть ли следующая страница.
        IReadOnlyList<PlanGrant> grants = await _grants.GetPlanGrantsKeysetAsync(
            plan.Id,
            userIdsFilter,
            cursorGrantedAt,
            cursorId,
            limit + 1,
            cancellationToken);

        bool hasNext = grants.Count > limit;
        IReadOnlyList<PlanGrant> page = hasNext ? grants.Take(limit).ToList() : grants;

        if (page.Count == 0)
        {
            return new PlanGrantsPageDto([], null);
        }

        Dictionary<Guid, AuthUserLookupDto> userById = await LoadUsersAsync(page, cancellationToken);

        IReadOnlyList<PlanGrantDto> dtos = page
            .Select(g =>
            {
                userById.TryGetValue(g.UserId, out AuthUserLookupDto? user);
                return PlanGrantMapper.MapToDtoWithUser(g, user);
            })
            .ToList();

        string? nextCursor = hasNext ? EncodeCursor(page[^1].GrantedAt, page[^1].Id) : null;
        return new PlanGrantsPageDto(dtos, nextCursor);
    }

    private static int NormalizeLimit(int? requested)
    {
        if (requested is null || requested.Value <= 0) return DEFAULT_LIMIT;
        return Math.Min(requested.Value, MAX_LIMIT);
    }

    private static string EncodeCursor(DateTimeOffset grantedAt, Guid id)
    {
        string raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{grantedAt.UtcTicks}:{id:N}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTimeOffset?, Guid?) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);

        try
        {
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            string[] parts = raw.Split(':', 2);
            if (parts.Length != 2) return (null, null);

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                return (null, null);
            if (!Guid.TryParseExact(parts[1], "N", out Guid id))
                return (null, null);

            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException)
        {
            // Invalid cursor — treat as no-cursor (start of feed) rather than 400.
            return (null, null);
        }
    }

    private async Task<Dictionary<Guid, AuthUserLookupDto>> LoadUsersAsync(
        IReadOnlyList<PlanGrant> grants,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> userIds = grants
            .Select(g => g.UserId)
            .Distinct()
            .ToList();

        Result<IReadOnlyList<AuthUserLookupDto>, Error> result =
            await _authClient.GetUsersByIdsAsync(userIds, cancellationToken);

        if (result.IsFailure)
        {
            // Soft-degrade: на отказе AuthService отдаём grants без enrichment'а
            // (UI покажет UUID-ы как раньше) — лучше чем уронить весь экран.
            _logger.LogWarning(
                "ListPlanGrants user enrichment failed: {ErrorMessage}",
                result.Error.GetMessage());
            return [];
        }

        return result.Value.ToDictionary(u => u.UserId);
    }
}
