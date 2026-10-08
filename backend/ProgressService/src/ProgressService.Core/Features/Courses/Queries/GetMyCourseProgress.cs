using System.Data.Common;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetMyCourseProgressQuery(string? Cursor, int Limit, Guid? AuthorId = null) : IQuery;

public sealed class GetMyCourseProgressQueryValidator : AbstractValidator<GetMyCourseProgressQuery>
{
    public GetMyCourseProgressQueryValidator()
    {
        RuleFor(x => x.Limit)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetMyCourseProgressQuery.Limit)));
    }
}

public sealed class GetMyCourseProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/my/progress", async Task<EndpointResult<CursorResponse<UserCourseProgress>>> (
                    [AsParameters] GetMyCourseProgressQuery query,
                    [FromServices] GetMyCourseProgressHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(query, cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
/// Derive-модель (epic access-derive-model, Phase 1): множество «мои курсы» выводится из
/// активных grant'ов пользователя через AccessService
/// (<see cref="IAccessServiceClient.GetUserCoveredCoursesAsync"/>), а НЕ из материализованных
/// <c>course_enrollments</c>. Это чинит #366 (новый курс автора не появлялся у lifetime-holder'ов).
/// Локальный прогресс джойнится LEFT JOIN'ом по courseId — covered-курс без enrollment-строки
/// читается как 0% / not started. Blueprints (title/totals/sort_key/Kind) приходят из ECS;
/// `Kind` сохранён для frontend-дедупликации интенсивов (!287 / #364).
/// </summary>
public sealed class GetMyCourseProgressHandler
    : IQueryHandlerWithResult<CursorResponse<UserCourseProgress>, GetMyCourseProgressQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IAccessServiceClient _accessServiceClient;
    private readonly UserScopedData _user;
    private readonly IValidator<GetMyCourseProgressQuery> _validator;

    public GetMyCourseProgressHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        IAccessServiceClient accessServiceClient,
        UserScopedData user,
        IValidator<GetMyCourseProgressQuery> validator)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _accessServiceClient = accessServiceClient;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<UserCourseProgress>, Error>> Handle(
        GetMyCourseProgressQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Cursor — offset-based int (base64). Order устанавливается по author-defined
        // `course.sort_key` (blueprint.SortKey) из ECS. Множество курсов берём из AccessService
        // covered-courses (derive из грантов), а не из course_enrollments — иначе lifetime-holder
        // не видит курс до первого engagement (баг #366).
        int offset = DecodeOffsetCursor(query.Cursor);

        Result<CoveredCoursesResult, Error> coveredResult =
            await _accessServiceClient.GetUserCoveredCoursesAsync(
                _user.UserId,
                query.AuthorId,
                cancellationToken);

        if (coveredResult.IsFailure)
        {
            return coveredResult.Error;
        }

        Guid[] courseIds = coveredResult.Value.CourseIds.Distinct().ToArray();
        long totalCount = courseIds.Length;

        if (courseIds.Length == 0)
        {
            return Result.Success<CursorResponse<UserCourseProgress>, Error>(
                new CursorResponse<UserCourseProgress>
                {
                    Items = [],
                    NextCursor = null,
                    TotalCount = totalCount
                });
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintsResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest(courseIds),
                cancellationToken);

        if (blueprintsResult.IsFailure)
        {
            return blueprintsResult.Error;
        }

        Dictionary<Guid, CourseProgressBlueprintDto> blueprintsMap = blueprintsResult.Value
            .ToDictionary(x => x.CourseId);

        // Локальный прогресс — LEFT JOIN по (userId, courseId). Covered-курс без enrollment-строки
        // = 0% / not started. enrollment_id может быть NULL (нет якоря) → issues/modules = 0,
        // материалы считаются по user-scoped material_views ∩ blueprint.MaterialIds в любом случае.
        Dictionary<Guid, EnrollmentRow> enrollmentByCourseId = await LoadEnrollmentsAsync(
            courseIds, query.AuthorId, cancellationToken);

        // Развёртываем (course_id, material_id) пары из blueprint'ов для UNNEST.
        List<Guid> pairCourseIds = [];
        List<Guid> pairMaterialIds = [];
        foreach (CourseProgressBlueprintDto bp in blueprintsResult.Value)
        {
            foreach (Guid materialId in bp.MaterialIds)
            {
                pairCourseIds.Add(bp.CourseId);
                pairMaterialIds.Add(materialId);
            }
        }

        // completed-материалы — user-scoped, считаются по courseId через material_views.
        Dictionary<Guid, MaterialCompletedRow> materialCompletedByCourse =
            await LoadMaterialCompletionAsync(pairCourseIds, pairMaterialIds, cancellationToken);

        // Квизы (ST-13 #493) — user-scoped passed-попытки ∩ blueprint.QuizIds, зеркало
        // материалов: blueprint.TotalItems включает квизы, числитель должен тоже.
        List<Guid> pairQuizCourseIds = [];
        List<Guid> pairQuizIds = [];
        foreach (CourseProgressBlueprintDto bp in blueprintsResult.Value)
        {
            foreach (Guid quizId in bp.QuizIds)
            {
                pairQuizCourseIds.Add(bp.CourseId);
                pairQuizIds.Add(quizId);
            }
        }

        Dictionary<Guid, int> quizzesPassedByCourse =
            await LoadQuizCompletionAsync(pairQuizCourseIds, pairQuizIds, cancellationToken);

        // issues/modules completed — enrollment-scoped. Только для курсов с enrollment-строкой.
        Guid[] enrollmentIds = enrollmentByCourseId.Values.Select(e => e.EnrollmentId).ToArray();
        Dictionary<Guid, EnrollmentProgressRow> progressByEnrollmentId =
            await LoadEnrollmentProgressAsync(enrollmentIds, cancellationToken);

        List<UserCourseProgress> items = [];

        foreach (Guid courseId in courseIds)
        {
            if (!blueprintsMap.TryGetValue(courseId, out CourseProgressBlueprintDto? blueprint))
            {
                // ECS не вернул blueprint для covered-курса (soft-deleted / гонка) — пропускаем,
                // не валим всю выборку 404'ом.
                continue;
            }

            enrollmentByCourseId.TryGetValue(courseId, out EnrollmentRow? enrollment);

            materialCompletedByCourse.TryGetValue(courseId, out MaterialCompletedRow? materialCompleted);
            int completedMaterials = materialCompleted?.CompletedMaterials ?? 0;
            DateTime? lastMaterialAt = materialCompleted?.LastMaterialAt;

            int completedIssues = 0;
            int completedModules = 0;
            DateTime? lastIssueAt = null;
            if (enrollment is not null &&
                progressByEnrollmentId.TryGetValue(enrollment.EnrollmentId, out EnrollmentProgressRow? progress))
            {
                completedIssues = progress.CompletedUniqueIssues;
                completedModules = progress.CompletedModules;
                lastIssueAt = progress.LastIssueAt;
            }

            quizzesPassedByCourse.TryGetValue(courseId, out int passedQuizzes);

            int completedItems = completedMaterials + completedIssues + passedQuizzes;
            int totalItems = blueprint.TotalItems;
            int progressPercent = totalItems == 0
                ? 0
                : (int)Math.Floor((decimal)completedItems / totalItems * 100);

            DateTime? lastActivityAt = MaxNullable(lastMaterialAt, lastIssueAt);

            items.Add(new UserCourseProgress(
                enrollment?.EnrollmentId ?? Guid.Empty,
                courseId,
                blueprint.CourseSlug,
                blueprint.Title,
                blueprint.Description,
                blueprint.ImageId,
                blueprint.ImageUrl,
                totalItems,
                completedItems,
                blueprint.TotalMaterials,
                completedMaterials,
                blueprint.TotalUniqueIssues,
                completedIssues,
                blueprint.TotalModules,
                completedModules,
                blueprint.TotalQuizzes,
                passedQuizzes,
                progressPercent,
                blueprint.IsNew,
                blueprint.SortKey,
                enrollment?.EnrolledAt ?? default,
                lastActivityAt,
                blueprint.Kind));
        }

        // Author-defined sort: по `course.sort_key` (ECS blueprint), затем по courseId
        // для стабильности при равных sort_key.
        List<UserCourseProgress> sortedItems = [.. items
            .OrderBy(i => blueprintsMap.TryGetValue(i.CourseId, out CourseProgressBlueprintDto? bp)
                ? bp.SortKey
                : "￿",
                StringComparer.Ordinal)
            .ThenBy(i => i.CourseId)];

        // Offset-based slice. Total = всех covered курсов (учитываем, что какие-то blueprint'ы
        // могли отвалиться — тогда totalCount чуть больше реально отданного; это honest edge,
        // фронт ориентируется на NextCursor.)
        List<UserCourseProgress> page = sortedItems
            .Skip(offset)
            .Take(query.Limit)
            .ToList();

        bool hasMore = offset + page.Count < sortedItems.Count;
        string? nextCursor = hasMore ? EncodeOffsetCursor(offset + page.Count) : null;

        return Result.Success<CursorResponse<UserCourseProgress>, Error>(
            new CursorResponse<UserCourseProgress>
            {
                Items = page,
                NextCursor = nextCursor,
                TotalCount = totalCount
            });
    }

    private async Task<Dictionary<Guid, EnrollmentRow>> LoadEnrollmentsAsync(
        Guid[] courseIds,
        Guid? authorId,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                ce.id AS EnrollmentId,
                ce.course_id AS CourseId,
                ce.enrolled_at AS EnrolledAt
            FROM course_enrollments ce
            WHERE ce.user_id = @UserId
              AND ce.course_id = ANY(@CourseIds)
              AND (@AuthorId IS NULL OR ce.author_id = @AuthorId)
            """;

        IEnumerable<EnrollmentRow> rows = await connection.QueryAsync<EnrollmentRow>(
            new CommandDefinition(
                sql,
                new { UserId = _user.UserId, CourseIds = courseIds, AuthorId = authorId },
                cancellationToken: cancellationToken));

        Dictionary<Guid, EnrollmentRow> map = [];
        foreach (EnrollmentRow row in rows)
        {
            map.TryAdd(row.CourseId, row);
        }

        return map;
    }

    private async Task<Dictionary<Guid, MaterialCompletedRow>> LoadMaterialCompletionAsync(
        List<Guid> pairCourseIds,
        List<Guid> pairMaterialIds,
        CancellationToken cancellationToken)
    {
        if (pairCourseIds.Count == 0)
        {
            return [];
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // is_completed=TRUE — только явно «Изучено» (issue #285); silent track-view'ы не
        // двигают счётчик прогресса курса.
        const string sql = """
            WITH course_material_pairs AS (
                SELECT
                    UNNEST(@PairCourseIds::uuid[]) AS course_id,
                    UNNEST(@PairMaterialIds::uuid[]) AS material_id
            )
            SELECT
                cmp.course_id AS CourseId,
                COUNT(*)::integer AS CompletedMaterials,
                MAX(COALESCE(mv.completed_at, mv.viewed_at)) AS LastMaterialAt
            FROM course_material_pairs cmp
            JOIN material_views mv
                ON mv.material_id = cmp.material_id
               AND mv.user_id = @UserId
               AND mv.is_completed = TRUE
            GROUP BY cmp.course_id
            """;

        IEnumerable<MaterialCompletedRow> rows = await connection.QueryAsync<MaterialCompletedRow>(
            new CommandDefinition(
                sql,
                new
                {
                    PairCourseIds = pairCourseIds.ToArray(),
                    PairMaterialIds = pairMaterialIds.ToArray(),
                    UserId = _user.UserId,
                },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(x => x.CourseId);
    }

    private async Task<Dictionary<Guid, int>> LoadQuizCompletionAsync(
        List<Guid> pairCourseIds,
        List<Guid> pairQuizIds,
        CancellationToken cancellationToken)
    {
        if (pairCourseIds.Count == 0)
        {
            return [];
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // Только passed-попытки: завершение элемента программы = проходной балл (ST-13 #493),
        // неуспешные попытки прогресс не двигают.
        const string sql = """
            WITH course_quiz_pairs AS (
                SELECT
                    UNNEST(@PairCourseIds::uuid[]) AS course_id,
                    UNNEST(@PairQuizIds::uuid[]) AS quiz_id
            )
            SELECT
                cqp.course_id AS CourseId,
                COUNT(DISTINCT cqp.quiz_id)::integer AS PassedQuizzes
            FROM course_quiz_pairs cqp
            JOIN quiz_attempts qa
                ON qa.quiz_id = cqp.quiz_id
               AND qa.user_id = @UserId
               AND qa.passed = TRUE
            GROUP BY cqp.course_id
            """;

        IEnumerable<QuizCompletedRow> rows = await connection.QueryAsync<QuizCompletedRow>(
            new CommandDefinition(
                sql,
                new
                {
                    PairCourseIds = pairCourseIds.ToArray(),
                    PairQuizIds = pairQuizIds.ToArray(),
                    UserId = _user.UserId,
                },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(x => x.CourseId, x => x.PassedQuizzes);
    }

    private async Task<Dictionary<Guid, EnrollmentProgressRow>> LoadEnrollmentProgressAsync(
        Guid[] enrollmentIds,
        CancellationToken cancellationToken)
    {
        if (enrollmentIds.Length == 0)
        {
            return [];
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            WITH requested AS (
                SELECT DISTINCT UNNEST(@EnrollmentIds::uuid[]) AS enrollment_id
            ),
            issue_completed AS (
                SELECT
                    ip.enrollment_id,
                    COUNT(DISTINCT ip.issue_id)::integer AS completed_unique_issues,
                    MAX(ip.completed_at) AS last_issue_at
                FROM issue_progress ip
                JOIN requested r ON r.enrollment_id = ip.enrollment_id
                WHERE ip.status = 'COMPLETED'
                GROUP BY ip.enrollment_id
            ),
            module_completed AS (
                SELECT
                    mp.enrollment_id,
                    COUNT(*)::integer AS completed_modules
                FROM module_progress mp
                JOIN requested r ON r.enrollment_id = mp.enrollment_id
                WHERE mp.status = 'COMPLETED'
                GROUP BY mp.enrollment_id
            )
            SELECT
                r.enrollment_id AS EnrollmentId,
                COALESCE(ic.completed_unique_issues, 0) AS CompletedUniqueIssues,
                COALESCE(mc.completed_modules, 0) AS CompletedModules,
                ic.last_issue_at AS LastIssueAt
            FROM requested r
            LEFT JOIN issue_completed ic ON ic.enrollment_id = r.enrollment_id
            LEFT JOIN module_completed mc ON mc.enrollment_id = r.enrollment_id
            """;

        IEnumerable<EnrollmentProgressRow> rows = await connection.QueryAsync<EnrollmentProgressRow>(
            new CommandDefinition(
                sql,
                new { EnrollmentIds = enrollmentIds },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(x => x.EnrollmentId);
    }

    private static DateTime? MaxNullable(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a.Value >= b.Value ? a : b;
    }

    private static int DecodeOffsetCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        try
        {
            string raw = System.Text.Encoding.UTF8.GetString(
                Microsoft.AspNetCore.Authentication.Base64UrlTextEncoder.Decode(cursor));
            return int.TryParse(raw, out int offset) ? Math.Max(0, offset) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string EncodeOffsetCursor(int offset) =>
        Microsoft.AspNetCore.Authentication.Base64UrlTextEncoder.Encode(
            System.Text.Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private sealed class EnrollmentRow
    {
        public Guid EnrollmentId { get; init; }
        public Guid CourseId { get; init; }
        public DateTime EnrolledAt { get; init; }
    }

    private sealed class MaterialCompletedRow
    {
        public Guid CourseId { get; init; }
        public int CompletedMaterials { get; init; }
        public DateTime? LastMaterialAt { get; init; }
    }

    private sealed class QuizCompletedRow
    {
        public Guid CourseId { get; init; }
        public int PassedQuizzes { get; init; }
    }

    private sealed class EnrollmentProgressRow
    {
        public Guid EnrollmentId { get; init; }
        public int CompletedUniqueIssues { get; init; }
        public int CompletedModules { get; init; }
        public DateTime? LastIssueAt { get; init; }
    }
}
