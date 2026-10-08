using System.Data.Common;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetLastActiveCourseQuery : IQuery;

public sealed class GetLastActiveCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/my/last-active",
                async Task<EndpointResult<LastActiveCourseResponse?>> (
                        GetLastActiveCourseHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetLastActiveCourseQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
/// Derive-модель (epic access-derive-model, Phase 1): кандидаты = последняя открытая позиция
/// (<c>course_positions</c>, приоритет 1) ∪ covered-courses из AccessService (приоритет 2).
/// Раньше fallback брался из <c>course_enrollments</c>; теперь — из грантов, чтобы lifetime-holder
/// без enrollment-строки тоже получал last-active по covered-набору. Прогресс по issues/modules
/// читается только если у курса есть enrollment-якорь; материалы — всегда user-scoped.
/// </summary>
public sealed class GetLastActiveCourseHandler
    : IQueryHandlerWithResult<LastActiveCourseResponse?, GetLastActiveCourseQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IAccessServiceClient _accessServiceClient;
    private readonly UserScopedData _user;

    public GetLastActiveCourseHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        IAccessServiceClient accessServiceClient,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _accessServiceClient = accessServiceClient;
        _user = user;
    }

    public async Task<Result<LastActiveCourseResponse?, Error>> Handle(
        GetLastActiveCourseQuery query,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Приоритет 1: курс, который пользователь последний раз ОТКРЫВАЛ (course_positions).
        // Позиция привязана к (user, course) и не требует enrollment-строки.
        const string positionSql = """
            SELECT
                cp.course_id    AS CourseId,
                cp.entity_type  AS PositionEntityType,
                cp.entity_id    AS PositionEntityId,
                cp.opened_at    AS PositionOpenedAt
            FROM course_positions cp
            WHERE cp.user_id = @UserId
            ORDER BY cp.opened_at DESC
            LIMIT 1;
            """;

        PositionRow? position = await connection.QueryFirstOrDefaultAsync<PositionRow>(
            new CommandDefinition(positionSql, new { UserId = _user.UserId },
                cancellationToken: cancellationToken));

        Guid candidateCourseId;
        CoursePositionDto? lastPosition = null;

        if (position is not null)
        {
            candidateCourseId = position.CourseId;
            lastPosition = position.PositionEntityType is null
                ? null
                : new CoursePositionDto(
                    position.PositionEntityType,
                    position.PositionEntityId!.Value,
                    position.PositionOpenedAt!.Value);
        }
        else
        {
            // Приоритет 2: fallback на covered-courses (derive из грантов). Берём «первый»
            // covered-курс по сортировке id — раньше брался последний enrolled по enrolled_at,
            // но без enrollment-строки этого сигнала нет; covered-набор детерминирован.
            Result<CoveredCoursesResult, Error> coveredResult =
                await _accessServiceClient.GetUserCoveredCoursesAsync(_user.UserId, null, cancellationToken);

            if (coveredResult.IsFailure)
            {
                return coveredResult.Error;
            }

            Guid fallback = coveredResult.Value.CourseIds.OrderBy(id => id).FirstOrDefault();
            if (fallback == Guid.Empty)
            {
                return Result.Success<LastActiveCourseResponse?, Error>(null);
            }

            candidateCourseId = fallback;
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([candidateCourseId]),
                cancellationToken);

        if (blueprintResult.IsFailure)
        {
            return blueprintResult.Error;
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        if (blueprint is null)
        {
            return Error.NotFound(
                "course.progress.blueprint.not.found",
                $"Course progress blueprint not found for course {candidateCourseId}");
        }

        // Локальный enrollment-якорь курса (если есть) — нужен для issue/module прогресса.
        const string enrollmentSql = """
            SELECT ce.id AS EnrollmentId, ce.enrolled_at AS EnrolledAt
            FROM course_enrollments ce
            WHERE ce.user_id = @UserId AND ce.course_id = @CourseId
            LIMIT 1;
            """;

        EnrollmentRow? enrollment = await connection.QueryFirstOrDefaultAsync<EnrollmentRow>(
            new CommandDefinition(enrollmentSql,
                new { UserId = _user.UserId, CourseId = candidateCourseId },
                cancellationToken: cancellationToken));

        Guid[] materialIds = blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray();

        // material-completion — user-scoped (material_views ∩ blueprint.MaterialIds).
        const string materialSql = """
            SELECT
                COUNT(*)::integer                            AS CompletedMaterials,
                MAX(COALESCE(mv.completed_at, mv.viewed_at)) AS LastMaterialAt
            FROM material_views mv
            WHERE mv.user_id = @UserId
              AND mv.material_id = ANY(@MaterialIds)
              AND mv.is_completed = TRUE;
            """;

        MaterialRow materialRow = await connection.QuerySingleAsync<MaterialRow>(
            new CommandDefinition(
                materialSql,
                new { UserId = _user.UserId, MaterialIds = materialIds },
                cancellationToken: cancellationToken));

        int completedIssues = 0;
        int completedModules = 0;
        DateTime? lastIssueAt = null;

        if (enrollment is not null)
        {
            const string enrollmentProgressSql = """
                SELECT
                    (SELECT COUNT(DISTINCT ip.issue_id)::integer
                     FROM issue_progress ip
                     WHERE ip.enrollment_id = @EnrollmentId AND ip.status = 'COMPLETED') AS CompletedUniqueIssues,
                    (SELECT MAX(ip.completed_at)
                     FROM issue_progress ip
                     WHERE ip.enrollment_id = @EnrollmentId AND ip.status = 'COMPLETED') AS LastIssueAt,
                    (SELECT COUNT(*)::integer
                     FROM module_progress mp
                     WHERE mp.enrollment_id = @EnrollmentId AND mp.status = 'COMPLETED') AS CompletedModules;
                """;

            EnrollmentProgressRow progress = await connection.QuerySingleAsync<EnrollmentProgressRow>(
                new CommandDefinition(
                    enrollmentProgressSql,
                    new { EnrollmentId = enrollment.EnrollmentId },
                    cancellationToken: cancellationToken));

            completedIssues = progress.CompletedUniqueIssues;
            completedModules = progress.CompletedModules;
            lastIssueAt = progress.LastIssueAt;
        }

        // Квизы (ST-13 #493) — user-scoped passed-попытки ∩ blueprint.QuizIds: blueprint.TotalItems
        // включает квизы модулей, числитель «N из M» учитывает их симметрично заданиям.
        int passedQuizzes = 0;
        if (blueprint.QuizIds.Count > 0)
        {
            Guid[] quizIds = blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();

            const string quizSql = """
                SELECT COUNT(DISTINCT qa.quiz_id)::integer
                FROM quiz_attempts qa
                WHERE qa.user_id = @UserId
                  AND qa.quiz_id = ANY(@QuizIds)
                  AND qa.passed = TRUE;
                """;

            passedQuizzes = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    quizSql,
                    new { UserId = _user.UserId, QuizIds = quizIds },
                    cancellationToken: cancellationToken));
        }

        int completedItems = materialRow.CompletedMaterials + completedIssues + passedQuizzes;
        int progressPercent = blueprint.TotalItems == 0
            ? 0
            : (int)Math.Floor((decimal)completedItems / blueprint.TotalItems * 100);

        DateTime? lastActivityAt = MaxNullable(materialRow.LastMaterialAt, lastIssueAt);
        DateTime enrolledAt = enrollment?.EnrolledAt ?? lastPosition?.OpenedAt ?? default;

        return new LastActiveCourseResponse(
            enrollment?.EnrollmentId ?? Guid.Empty,
            candidateCourseId,
            blueprint.CourseSlug,
            blueprint.Title,
            blueprint.Description,
            blueprint.ImageId,
            blueprint.ImageUrl,
            blueprint.TotalItems,
            completedItems,
            blueprint.TotalMaterials,
            materialRow.CompletedMaterials,
            blueprint.TotalUniqueIssues,
            completedIssues,
            blueprint.TotalModules,
            completedModules,
            progressPercent,
            blueprint.IsNew,
            enrolledAt,
            lastActivityAt,
            lastPosition,
            blueprint.Kind);
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

    private sealed class PositionRow
    {
        public Guid CourseId { get; init; }
        public string? PositionEntityType { get; init; }
        public Guid? PositionEntityId { get; init; }
        public DateTime? PositionOpenedAt { get; init; }
    }

    private sealed class EnrollmentRow
    {
        public Guid EnrollmentId { get; init; }
        public DateTime EnrolledAt { get; init; }
    }

    private sealed class MaterialRow
    {
        public int CompletedMaterials { get; init; }
        public DateTime? LastMaterialAt { get; init; }
    }

    private sealed class EnrollmentProgressRow
    {
        public int CompletedUniqueIssues { get; init; }
        public int CompletedModules { get; init; }
        public DateTime? LastIssueAt { get; init; }
    }
}
