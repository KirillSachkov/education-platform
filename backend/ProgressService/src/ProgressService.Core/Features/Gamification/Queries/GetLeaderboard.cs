using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.Domain.Issues;

namespace ProgressService.Core.Features.Gamification.Queries;

// NOTE: Leaderboard pagination is intentionally offset-based (page/pageSize), not cursor-based.
// The leaderboard is a stable ranking view computed via ROW_NUMBER() OVER
// (ORDER BY total_xp DESC, updated_at ASC, user_id ASC), and the UI lets users navigate by page
// numbers + rank. Cursor pagination doesn't compose well with visible rank numbers (ranks shift
// as XP changes), and the 60s HybridCache TTL makes any within-page drift invisible.
// Keep as offset — do not migrate to keyset.
public sealed record GetLeaderboardQuery(int Page, int PageSize, Guid? AuthorId = null, Guid? CourseId = null) : IQuery;

public sealed class GetLeaderboardQueryValidator : AbstractValidator<GetLeaderboardQuery>
{
    public GetLeaderboardQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithError(GeneralErrors.ValueIsInvalid("page"));

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .WithError(GeneralErrors.ValueIsInvalid("pageSize"));
    }
}

public sealed class GetLeaderboardEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/leaderboard", async Task<EndpointResult<GetLeaderboardResponse>> (
                    int? page,
                    int? pageSize,
                    Guid? authorId,
                    Guid? courseId,
                    GetLeaderboardHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetLeaderboardQuery(page ?? 1, pageSize ?? 20, authorId, courseId),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY);
}

public sealed class GetLeaderboardHandler
    : IQueryHandlerWithResult<GetLeaderboardResponse, GetLeaderboardQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15),
    };

    private static readonly HybridCacheEntryOptions _currentUserRankCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(10),
        LocalCacheExpiration = TimeSpan.FromSeconds(5),
    };

    // ---- Global leaderboard (no author filter) ----
    //
    // Issue #217 — was: COUNT(*) + SELECT in two statements via QueryMultipleAsync.
    // Now: single SELECT with COUNT(*) OVER() window → total_count piggy-backs on each row.
    // Halves planner work on the underlying scan; for the global case the win is small
    // (the table is already indexed), but the shape mirrors the per-author / per-course
    // variants where the CTE is heavy.

    // Issue #552 — SQL отдаёт глобальный total_xp (GlobalTotalXp), а не денорм-колонку
    // current_level: денорм пересчитывается только на XP-award и навсегда стейлится после
    // расширения кривой уровней. CurrentLevel вычисляется в handler'е через IXpLevelPolicy
    // (как в GetMyXpProgress / GetUserXpProgress).
    private const string LEADERBOARD_PAGE_SQL = """
        WITH ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (
                        ORDER BY ugs.total_xp DESC, ugs.updated_at ASC, ugs.user_id ASC
                    ) AS integer
                ) AS rank,
                ugs.user_id AS user_id,
                pu.display_name AS display_name,
                pu.username AS username,
                ugs.total_xp AS total_xp
            FROM user_gamification_stats ugs
            LEFT JOIN progress_users pu ON pu.user_id = ugs.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            total_xp AS GlobalTotalXp,
            FALSE AS IsCurrentUser,
            (SELECT COUNT(*) FROM user_gamification_stats)::integer AS TotalCount
        FROM ranked
        ORDER BY rank
        LIMIT @PageSize OFFSET @Offset;
        """;

    private const string CURRENT_USER_SQL = """
        WITH ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (
                        ORDER BY ugs.total_xp DESC, ugs.updated_at ASC, ugs.user_id ASC
                    ) AS integer
                ) AS rank,
                ugs.user_id AS user_id,
                pu.display_name AS display_name,
                pu.username AS username,
                ugs.total_xp AS total_xp
            FROM user_gamification_stats ugs
            LEFT JOIN progress_users pu ON pu.user_id = ugs.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            total_xp AS GlobalTotalXp,
            TRUE AS IsCurrentUser,
            0 AS TotalCount
        FROM ranked
        WHERE user_id = @CurrentUserId
        LIMIT 1
        """;

    // ---- Per-author leaderboard (scoped to author's courses) ----
    //
    // Issue #217 — was: same `author_xp` CTE written and executed twice (once for COUNT,
    // once for the page). The CTE aggregates `xp_awards` filtered by an `OR (... EXISTS ...)`
    // predicate — a known PG-planner pessimization that often degrades to a seqscan on
    // `xp_awards`. Two changes:
    //   1. Single CTE. `COUNT(*) OVER()` returns the total alongside each ranked row.
    //   2. The `xp_awards` filter is rewritten as `UNION ALL` of two index-friendly
    //      branches: enrollment-bound awards (via `enrollment_id = ANY(...)`) and
    //      user-scoped awards (`enrollment_id IS NULL` for MATERIAL_VIEWED, 2026-04-22).
    //      Both branches reuse the pre-computed `author_enrollments` and `author_users`
    //      CTEs so the planner only walks `course_enrollments` once.
    //
    // The duplicate-row hazard from UNION ALL is impossible by construction: an
    // `xp_awards` row has either a non-null `enrollment_id` (matches branch 1) OR a
    // null `enrollment_id` (matches branch 2), never both.
    private const string AUTHOR_LEADERBOARD_PAGE_SQL = """
        WITH author_enrollments AS (
            SELECT ce.id, ce.user_id
            FROM course_enrollments ce
            WHERE ce.author_id = @AuthorId
        ),
        author_users AS (
            SELECT DISTINCT user_id FROM author_enrollments
        ),
        author_xp AS (
            SELECT user_id, SUM(xp_amount) AS total_xp
            FROM (
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id = ANY (SELECT id FROM author_enrollments)
                UNION ALL
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id IS NULL
                  AND xa.user_id = ANY (SELECT user_id FROM author_users)
            ) combined
            GROUP BY user_id
        ),
        ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (ORDER BY ax.total_xp DESC, ax.user_id ASC)
                    AS integer
                ) AS rank,
                ax.user_id,
                pu.display_name,
                pu.username,
                ax.total_xp,
                COALESCE(ugs.total_xp, 0) AS global_total_xp,
                CAST(COUNT(*) OVER () AS integer) AS total_count
            FROM author_xp ax
            LEFT JOIN progress_users pu ON pu.user_id = ax.user_id
            LEFT JOIN user_gamification_stats ugs ON ugs.user_id = ax.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            global_total_xp AS GlobalTotalXp,
            FALSE AS IsCurrentUser,
            total_count AS TotalCount
        FROM ranked
        ORDER BY rank
        LIMIT @PageSize OFFSET @Offset;
        """;

    // ---- Per-course leaderboard (scoped to a single course) ----
    //
    // Issue #217 — same refactor as AUTHOR_LEADERBOARD_PAGE_SQL above:
    //   1. One CTE pass via `COUNT(*) OVER()` instead of two.
    //   2. `OR (... EXISTS ...)` → `UNION ALL` over enrollment-bound + user-scoped
    //      `xp_awards` branches, both keyed off pre-computed CTEs.
    private const string COURSE_LEADERBOARD_PAGE_SQL = """
        WITH course_enrollments_scope AS (
            SELECT ce.id, ce.user_id
            FROM course_enrollments ce
            WHERE ce.course_id = @CourseId
        ),
        course_users AS (
            SELECT DISTINCT user_id FROM course_enrollments_scope
        ),
        course_xp AS (
            SELECT user_id, SUM(xp_amount) AS total_xp
            FROM (
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id = ANY (SELECT id FROM course_enrollments_scope)
                UNION ALL
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id IS NULL
                  AND xa.user_id = ANY (SELECT user_id FROM course_users)
            ) combined
            GROUP BY user_id
        ),
        ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (ORDER BY cx.total_xp DESC, cx.user_id ASC)
                    AS integer
                ) AS rank,
                cx.user_id,
                pu.display_name,
                pu.username,
                cx.total_xp,
                COALESCE(ugs.total_xp, 0) AS global_total_xp,
                CAST(COUNT(*) OVER () AS integer) AS total_count
            FROM course_xp cx
            LEFT JOIN progress_users pu ON pu.user_id = cx.user_id
            LEFT JOIN user_gamification_stats ugs ON ugs.user_id = cx.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            global_total_xp AS GlobalTotalXp,
            FALSE AS IsCurrentUser,
            total_count AS TotalCount
        FROM ranked
        ORDER BY rank
        LIMIT @PageSize OFFSET @Offset;
        """;

    // Issue #217 — same UNION ALL rewrite as the per-course page query.
    // `LeaderboardRow.TotalCount` is unused here (single-row lookup), but the column is
    // emitted to keep one Dapper materializer across all three rank-lookup SQLs.
    private const string COURSE_CURRENT_USER_SQL = """
        WITH course_enrollments_scope AS (
            SELECT ce.id, ce.user_id
            FROM course_enrollments ce
            WHERE ce.course_id = @CourseId
        ),
        course_users AS (
            SELECT DISTINCT user_id FROM course_enrollments_scope
        ),
        course_xp AS (
            SELECT user_id, SUM(xp_amount) AS total_xp
            FROM (
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id = ANY (SELECT id FROM course_enrollments_scope)
                UNION ALL
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id IS NULL
                  AND xa.user_id = ANY (SELECT user_id FROM course_users)
            ) combined
            GROUP BY user_id
        ),
        ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (ORDER BY cx.total_xp DESC, cx.user_id ASC)
                    AS integer
                ) AS rank,
                cx.user_id,
                pu.display_name,
                pu.username,
                cx.total_xp,
                COALESCE(ugs.total_xp, 0) AS global_total_xp
            FROM course_xp cx
            LEFT JOIN progress_users pu ON pu.user_id = cx.user_id
            LEFT JOIN user_gamification_stats ugs ON ugs.user_id = cx.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            global_total_xp AS GlobalTotalXp,
            TRUE AS IsCurrentUser,
            0 AS TotalCount
        FROM ranked
        WHERE user_id = @CurrentUserId
        LIMIT 1
        """;

    // Issue #217 — same UNION ALL rewrite as the per-author page query.
    private const string AUTHOR_CURRENT_USER_SQL = """
        WITH author_enrollments AS (
            SELECT ce.id, ce.user_id
            FROM course_enrollments ce
            WHERE ce.author_id = @AuthorId
        ),
        author_users AS (
            SELECT DISTINCT user_id FROM author_enrollments
        ),
        author_xp AS (
            SELECT user_id, SUM(xp_amount) AS total_xp
            FROM (
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id = ANY (SELECT id FROM author_enrollments)
                UNION ALL
                SELECT xa.user_id, xa.xp_amount
                FROM xp_awards xa
                WHERE xa.enrollment_id IS NULL
                  AND xa.user_id = ANY (SELECT user_id FROM author_users)
            ) combined
            GROUP BY user_id
        ),
        ranked AS (
            SELECT
                CAST(
                    ROW_NUMBER() OVER (ORDER BY ax.total_xp DESC, ax.user_id ASC)
                    AS integer
                ) AS rank,
                ax.user_id,
                pu.display_name,
                pu.username,
                ax.total_xp,
                COALESCE(ugs.total_xp, 0) AS global_total_xp
            FROM author_xp ax
            LEFT JOIN progress_users pu ON pu.user_id = ax.user_id
            LEFT JOIN user_gamification_stats ugs ON ugs.user_id = ax.user_id
        )
        SELECT
            rank AS Rank,
            user_id AS UserId,
            display_name AS DisplayName,
            username AS Username,
            total_xp AS TotalXp,
            global_total_xp AS GlobalTotalXp,
            TRUE AS IsCurrentUser,
            0 AS TotalCount
        FROM ranked
        WHERE user_id = @CurrentUserId
        LIMIT 1
        """;

    // ---- Global activity stats (issue #572) ----
    //
    // Глобальные счётчики активности участника по всей платформе (не scoped по
    // курсу/автору — согласовано с владельцем; курсовой рейтинг убран в #572):
    //   - MaterialsCompleted — изученные материалы (material_views.is_completed = TRUE).
    //   - QuizzesCompleted   — пройденные тесты (distinct passed quiz_attempts; level-test
    //                          живёт в отдельной таблице level_test_attempts → не попадает).
    //   - IssuesCompleted    — решённые задачи (distinct COMPLETED issue_progress через enrollment).
    //
    // Батчится по userIds текущей страницы (≤100) на шаге обогащения, под тем же 60s
    // HybridCache, что и сама страница. unnest(@UserIds) гарантирует строку на каждый
    // userId (0 при отсутствии активности); COUNT → bigint, поэтому явный CAST в integer.
    private const string USER_ACTIVITY_STATS_SQL = """
        SELECT
            u.user_id AS UserId,
            CAST(COALESCE(m.cnt, 0) AS integer) AS MaterialsCompleted,
            CAST(COALESCE(q.cnt, 0) AS integer) AS QuizzesCompleted,
            CAST(COALESCE(i.cnt, 0) AS integer) AS IssuesCompleted
        FROM unnest(@UserIds) AS u(user_id)
        LEFT JOIN (
            SELECT user_id, COUNT(*) AS cnt
            FROM material_views
            WHERE is_completed = TRUE AND user_id = ANY (@UserIds)
            GROUP BY user_id
        ) m ON m.user_id = u.user_id
        LEFT JOIN (
            SELECT user_id, COUNT(DISTINCT quiz_id) AS cnt
            FROM quiz_attempts
            WHERE passed = TRUE AND user_id = ANY (@UserIds)
            GROUP BY user_id
        ) q ON q.user_id = u.user_id
        LEFT JOIN (
            SELECT ce.user_id, COUNT(DISTINCT ip.issue_id) AS cnt
            FROM issue_progress ip
            JOIN course_enrollments ce ON ce.id = ip.enrollment_id
            WHERE ip.status = @CompletedStatus AND ce.user_id = ANY (@UserIds)
            GROUP BY ce.user_id
        ) i ON i.user_id = u.user_id;
        """;

    private static readonly IReadOnlyDictionary<Guid, UserActivityStats> _emptyStats =
        new Dictionary<Guid, UserActivityStats>();

    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly UserScopedData _user;
    private readonly HybridCache _cache;
    private readonly IXpLevelPolicy _xpLevelPolicy;
    private readonly IValidator<GetLeaderboardQuery> _validator;
    private readonly ILogger<GetLeaderboardHandler> _logger;

    public GetLeaderboardHandler(
        ITransactionManager transactionManager,
        IAuthServiceClient authServiceClient,
        UserScopedData user,
        HybridCache cache,
        IXpLevelPolicy xpLevelPolicy,
        IValidator<GetLeaderboardQuery> validator,
        ILogger<GetLeaderboardHandler> logger)
    {
        _transactionManager = transactionManager;
        _authServiceClient = authServiceClient;
        _user = user;
        _cache = cache;
        _xpLevelPolicy = xpLevelPolicy;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<GetLeaderboardResponse, Error>> Handle(
        GetLeaderboardQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        string cacheKey = query.CourseId.HasValue
            ? $"leaderboard:course:{query.CourseId.Value}:{query.Page}:{query.PageSize}"
            : query.AuthorId.HasValue
                ? $"leaderboard:{query.AuthorId.Value}:{query.Page}:{query.PageSize}"
                : $"leaderboard:global:{query.Page}:{query.PageSize}";

        CachedLeaderboardPage page = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct => await FetchLeaderboardPageEnriched(query, ct),
            _cacheOptions,
            cancellationToken: cancellationToken);

        IReadOnlyList<LeaderboardUserDto> items = page.Items;
        int totalCount = page.TotalCount;
        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling((double)totalCount / query.PageSize);

        LeaderboardUserDto? currentUser = null;
        bool isAuthenticated = _user.UserId != Guid.Empty;

        if (isAuthenticated)
        {
            string currentUserCacheKey = query.CourseId.HasValue
                ? $"leaderboard:user-rank:course:{query.CourseId.Value}:{_user.UserId}"
                : query.AuthorId.HasValue
                    ? $"leaderboard:user-rank:{query.AuthorId.Value}:{_user.UserId}"
                    : $"leaderboard:user-rank:global:{_user.UserId}";

            CachedCurrentUserRank cached = await _cache.GetOrCreateAsync(
                currentUserCacheKey,
                async ct => new CachedCurrentUserRank(await FetchCurrentUserRank(query.AuthorId, query.CourseId, ct)),
                _currentUserRankCacheOptions,
                cancellationToken: cancellationToken);

            currentUser = cached.Row;
        }

        // Mark items that belong to the current user
        if (isAuthenticated)
        {
            items = items.Select(i =>
                i.UserId == _user.UserId ? i with { IsCurrentUser = true } : i).ToList();
        }

        return new GetLeaderboardResponse(
            items,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages,
            currentUser);
    }

    private async Task<CachedLeaderboardPage> FetchLeaderboardPageEnriched(
        GetLeaderboardQuery query, CancellationToken cancellationToken)
    {
        CachedLeaderboardPage page = await FetchLeaderboardPage(query, cancellationToken);

        if (page.Items.Count == 0)
        {
            return page;
        }

        List<Guid> userIds = page.Items.Select(i => i.UserId).ToList();

        // Issue #572 — глобальная активность участников (батч по userIds страницы).
        IReadOnlyDictionary<Guid, UserActivityStats> statsByUser =
            await FetchUserActivityStats(userIds, cancellationToken);

        // Профили из AuthService (имя/аватар) — soft-degradation при недоступности:
        // лидерборд всё равно рендерится с данными projection-таблицы progress_users.
        Dictionary<Guid, AuthUserLookupDto>? userById = null;
        Result<IReadOnlyList<AuthUserLookupDto>, Error> usersResult =
            await _authServiceClient.GetUsersByIdsAsync(userIds, cancellationToken);
        if (usersResult.IsSuccess)
        {
            userById = usersResult.Value.ToDictionary(u => u.UserId);
        }
        else
        {
            // Log so prolonged AuthService outage is visible beyond the 60s cache window.
            _logger.LogWarning(
                "Failed to enrich leaderboard with AuthService data: {ErrorType}",
                usersResult.Error.Type);
        }

        IReadOnlyList<LeaderboardUserDto> enrichedItems = page.Items.Select(i =>
        {
            UserActivityStats stats = statsByUser.GetValueOrDefault(i.UserId);

            LeaderboardUserDto item = i with
            {
                MaterialsCompleted = stats.MaterialsCompleted,
                QuizzesCompleted = stats.QuizzesCompleted,
                IssuesCompleted = stats.IssuesCompleted,
            };

            if (userById is not null && userById.TryGetValue(i.UserId, out AuthUserLookupDto? u))
            {
                item = item with
                {
                    AvatarId = u.AvatarId,
                    DisplayName = u.Name ?? item.DisplayName,
                    Username = u.Username ?? item.Username,
                };
            }

            return item;
        }).ToList();

        return new CachedLeaderboardPage(enrichedItems, page.TotalCount);
    }

    // Issue #572 — глобальная активность участников по userIds. Возвращает словарь
    // (отсутствующий userId → нулевая статистика). Под тем же кэшем, что и страница.
    private async Task<IReadOnlyDictionary<Guid, UserActivityStats>> FetchUserActivityStats(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return _emptyStats;
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        IReadOnlyList<UserActivityStatsRow> rows = (await connection.QueryAsync<UserActivityStatsRow>(
                new CommandDefinition(USER_ACTIVITY_STATS_SQL,
                    new
                    {
                        UserIds = userIds.ToArray(),
                        CompletedStatus = nameof(IssueProgressStatus.COMPLETED),
                    },
                    cancellationToken: cancellationToken)))
            .ToList();

        return rows.ToDictionary(
            r => r.UserId,
            r => new UserActivityStats(r.MaterialsCompleted, r.QuizzesCompleted, r.IssuesCompleted));
    }

    private async Task<CachedLeaderboardPage> FetchLeaderboardPage(
        GetLeaderboardQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();
        int offset = (query.Page - 1) * query.PageSize;

        string sql = query.CourseId.HasValue
            ? COURSE_LEADERBOARD_PAGE_SQL
            : query.AuthorId.HasValue
                ? AUTHOR_LEADERBOARD_PAGE_SQL
                : LEADERBOARD_PAGE_SQL;

        // Issue #217 — single query, `COUNT(*) OVER()` piggy-backs on every row as `TotalCount`.
        // Empty result set → total = 0 (no rows to read it from). Caller still computes
        // `totalPages` correctly in that branch.
        IReadOnlyList<LeaderboardRow> rows = (await connection.QueryAsync<LeaderboardRow>(
                new CommandDefinition(sql,
                    new
                    {
                        query.PageSize,
                        Offset = offset,
                        AuthorId = query.AuthorId ?? Guid.Empty,
                        CourseId = query.CourseId ?? Guid.Empty,
                    },
                    cancellationToken: cancellationToken)))
            .ToList();

        int totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        IReadOnlyList<LeaderboardUserDto> items = rows
            .Select(r => new LeaderboardUserDto(
                r.Rank, r.UserId, r.DisplayName, r.Username,
                r.TotalXp, _xpLevelPolicy.ResolveLevel(r.GlobalTotalXp), r.IsCurrentUser))
            .ToList();

        return new CachedLeaderboardPage(items, totalCount);
    }

    private async Task<LeaderboardUserDto?> FetchCurrentUserRank(
        Guid? authorId, Guid? courseId, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        string sql = courseId.HasValue
            ? COURSE_CURRENT_USER_SQL
            : authorId.HasValue
                ? AUTHOR_CURRENT_USER_SQL
                : CURRENT_USER_SQL;

        LeaderboardRow? row = await connection.QueryFirstOrDefaultAsync<LeaderboardRow>(
            new CommandDefinition(sql,
                new
                {
                    CurrentUserId = _user.UserId,
                    AuthorId = authorId ?? Guid.Empty,
                    CourseId = courseId ?? Guid.Empty,
                },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        LeaderboardUserDto dto = new(
            row.Rank, row.UserId, row.DisplayName, row.Username,
            row.TotalXp, _xpLevelPolicy.ResolveLevel(row.GlobalTotalXp), row.IsCurrentUser);

        // Issue #572 — те же глобальные счётчики, что и у строк таблицы (карточка «Ваше место»).
        IReadOnlyDictionary<Guid, UserActivityStats> stats =
            await FetchUserActivityStats([row.UserId], cancellationToken);
        UserActivityStats s = stats.GetValueOrDefault(row.UserId);

        return dto with
        {
            MaterialsCompleted = s.MaterialsCompleted,
            QuizzesCompleted = s.QuizzesCompleted,
            IssuesCompleted = s.IssuesCompleted,
        };
    }

    // Кешируется с уже вычисленным CurrentLevel — кривая уровней меняется только
    // с рестартом сервиса (GamificationOptions без hot-reload), кэш при этом сбрасывается.
    private sealed record CachedLeaderboardPage(
        IReadOnlyList<LeaderboardUserDto> Items,
        int TotalCount);

    private sealed record CachedCurrentUserRank(LeaderboardUserDto? Row);

    // Issue #572 — глобальные счётчики активности участника (uroki/testy/zadachi).
    private readonly record struct UserActivityStats(
        int MaterialsCompleted,
        int QuizzesCompleted,
        int IssuesCompleted);

    private sealed class UserActivityStatsRow
    {
        public Guid UserId { get; init; }
        public int MaterialsCompleted { get; init; }
        public int QuizzesCompleted { get; init; }
        public int IssuesCompleted { get; init; }
    }

    private sealed class LeaderboardRow
    {
        public int Rank { get; init; }
        public Guid UserId { get; init; }
        public string? DisplayName { get; init; }
        public string? Username { get; init; }
        public int TotalXp { get; init; }
        // Issue #552 — глобальный total_xp юзера (в scoped-вариантах отличается от TotalXp,
        // который содержит scoped-сумму). Источник для read-time вычисления уровня.
        public int GlobalTotalXp { get; init; }
        public bool IsCurrentUser { get; init; }

        // Issue #217 — `COUNT(*) OVER()` piggy-back on every page row; identical across rows.
        // Unused for current-user single-row lookups (SQL emits 0).
        public int TotalCount { get; init; }
    }
}
