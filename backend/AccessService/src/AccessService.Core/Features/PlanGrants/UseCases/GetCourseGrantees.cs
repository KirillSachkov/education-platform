using System.Globalization;
using System.Text;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// Service-to-service endpoint: keyset-страница пользователей, чей активный grant
/// покрывает <c>courseId</c> (global FULL/LEARN ∪ COURSE-of-course; FREE исключён).
/// Источник roster'а "кто на курсе X" в derive-модели (epic access-derive-model,
/// Phase 0). Caller (ProgressService GetCourseStudents) передаёт <c>authorId</c> как hint;
/// handler РЕЗОЛВИТ authoritative <c>authorId</c> server-side из <c>courseId</c> через
/// кешированный ECS-клиент. На рассинхрон caller-hint'а с резолвом — 400, чтобы
/// маршрут оставался привязан к реальному курсу. Опциональный name-search резолвится
/// caller'ом в <c>userIds</c> и передаётся через query <c>userIds=</c>.
/// </summary>
public sealed class GetCourseGranteesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/access/courses/{courseId:guid}/grantees", async Task<EndpointResult<CourseGranteesPage>> (
                [FromRoute] Guid courseId,
                [FromQuery] Guid authorId,
                [FromQuery] string? cursor,
                [FromQuery] int? limit,
                [FromQuery] Guid[]? userIds,
                [FromServices] GetCourseGranteesHandler handler,
                CancellationToken ct) =>
                await handler.Handle(
                    new GetCourseGranteesQuery(courseId, authorId, cursor, limit, userIds),
                    ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetCourseGranteesQuery(
    Guid CourseId,
    Guid AuthorId,
    string? Cursor,
    int? Limit,
    IReadOnlyList<Guid>? UserIds) : IQuery;

public sealed class GetCourseGranteesHandler
    : IQueryHandlerWithResult<CourseGranteesPage, GetCourseGranteesQuery>
{
    private const int DEFAULT_LIMIT = 20;
    private const int MAX_LIMIT = 100;

    private readonly IPlanGrantsRepository _grants;
    private readonly IEducationContentServiceClient _eduClient;

    public GetCourseGranteesHandler(
        IPlanGrantsRepository grants,
        IEducationContentServiceClient eduClient)
    {
        _grants = grants;
        _eduClient = eduClient;
    }

    public async Task<Result<CourseGranteesPage, Error>> Handle(
        GetCourseGranteesQuery query,
        CancellationToken cancellationToken = default)
    {
        // Resolve the authoritative authorId server-side from courseId (cached ECS lookup).
        // The caller-supplied authorId is only a hint; keeping the mismatch guard preserves
        // the route contract even though FULL/LEARN coverage is now global.
        Result<CourseDto, Error> courseLookup = await _eduClient
            .GetCourseLookupAsync(query.CourseId, cancellationToken);
        if (courseLookup.IsFailure)
        {
            return courseLookup.Error;
        }

        Guid resolvedAuthorId = courseLookup.Value.AuthorId;
        if (query.AuthorId != Guid.Empty && query.AuthorId != resolvedAuthorId)
        {
            return AccessErrors.CourseAuthorMismatch();
        }

        IReadOnlyList<Guid>? userIdsFilter = query.UserIds is { Count: > 0 } ? query.UserIds : null;

        int limit = NormalizeLimit(query.Limit);
        (DateTimeOffset? cursorGrantedAt, Guid? cursorId) = DecodeCursor(query.Cursor);

        // Берём limit+1 чтобы понять есть ли следующая страница.
        IReadOnlyList<CourseGranteeRow> rows = await _grants.GetCourseGranteesKeysetAsync(
            query.CourseId,
            resolvedAuthorId,
            userIdsFilter,
            cursorGrantedAt,
            cursorId,
            limit + 1,
            cancellationToken);

        int totalCount = await _grants.CountCourseGranteesAsync(
            query.CourseId,
            resolvedAuthorId,
            userIdsFilter,
            cancellationToken);

        bool hasNext = rows.Count > limit;
        IReadOnlyList<CourseGranteeRow> page = hasNext ? rows.Take(limit).ToList() : rows;

        IReadOnlyList<CourseGranteeDto> items = page
            .Select(r => new CourseGranteeDto(
                r.UserId,
                r.Source.ToString(),
                r.GrantedAt,
                r.PlanTier.ToString(),
                r.GrantId))
            .ToList();

        // Cursor encodes the keyset tuple (GrantedAt, UserId) — the repository keysets on
        // the user's latest covering-grant timestamp + UserId tiebreaker (Postgres has no
        // max(uuid), so the roster cannot keyset on GrantId).
        string? nextCursor = hasNext ? EncodeCursor(page[^1].GrantedAt, page[^1].UserId) : null;
        return new CourseGranteesPage(items, nextCursor, totalCount);
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
}
