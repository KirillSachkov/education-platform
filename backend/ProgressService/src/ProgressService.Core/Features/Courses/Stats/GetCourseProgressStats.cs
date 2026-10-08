using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Stats;

public sealed record GetCourseProgressStatsQuery(Guid CourseId) : IQuery;

public sealed class GetCourseProgressStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/courses/{courseId:guid}/stats/progress/",
                async Task<EndpointResult<CourseProgressStatsResponse>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseProgressStatsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetCourseProgressStatsQuery(courseId), ct))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
}

/// <summary>
///     Author per-course аналитика прогресса (#634): вовлечённые/завершившие студенты +
///     средний прогресс (та же CTE, что и публичный <c>GetCoursePublicStats</c>, но под
///     ownership-гейтом, без анонимного rate-limit'а) плюс три author-only счётчика —
///     отметок «изучено» по материалам курса, сдач заданий и студентов, сдавших хотя бы
///     один тест курса. Материалы/квизы курса берутся из blueprint (PUBLISHED-only).
///     Курс без активности → 200 с нулями. Read-only Dapper.
/// </summary>
public sealed class GetCourseProgressStatsHandler
    : IQueryHandlerWithResult<CourseProgressStatsResponse, GetCourseProgressStatsQuery>
{
    // Зеркало StaffIssueCompletionService.MANUAL_COMPLETION_PAYLOAD (private там) —
    // sentinel синтетических submission'ов ручной приёмки staff'ом, не реальная сдача.
    private const string MANUAL_COMPLETION_PAYLOAD = "https://sachkov-learn.net/manual-completion";

    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly UserScopedData _user;

    public GetCourseProgressStatsHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _user = user;
    }

    public async Task<Result<CourseProgressStatsResponse, Error>> Handle(
        GetCourseProgressStatsQuery query,
        CancellationToken cancellationToken)
    {
        Result<CourseDto, Error> authResult = await CourseStatsAuthorization.AuthorizeCourseManagementAsync(
            _educationContentServiceClient, _user, query.CourseId, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([query.CourseId]),
                cancellationToken);
        if (blueprintResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        int totalMaterials = blueprint?.TotalMaterials ?? 0;
        Guid[] materialIds = blueprint is null
            ? []
            : blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray();
        Guid[] quizIds = blueprint is null
            ? []
            : blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            WITH engaged_users AS (
                SELECT DISTINCT mv.user_id
                FROM material_views mv
                WHERE mv.material_id = ANY(@MaterialIds)
                  AND mv.is_completed = TRUE
                UNION
                SELECT DISTINCT ce.user_id
                FROM course_enrollments ce
                WHERE ce.course_id = @CourseId
                  AND (
                      EXISTS (SELECT 1 FROM issue_progress ip WHERE ip.enrollment_id = ce.id)
                   OR EXISTS (SELECT 1 FROM module_progress mp WHERE mp.enrollment_id = ce.id)
                  )
            ),
            per_user_viewed AS (
                SELECT
                    eu.user_id,
                    COUNT(mv.id) AS viewed
                FROM engaged_users eu
                LEFT JOIN material_views mv
                    ON mv.user_id = eu.user_id
                   AND mv.material_id = ANY(@MaterialIds)
                   AND mv.is_completed = TRUE
                GROUP BY eu.user_id
            )
            SELECT
                (SELECT COUNT(*) FROM engaged_users) AS EngagedStudents,
                (SELECT COUNT(*) FROM per_user_viewed
                     WHERE @TotalMaterials > 0
                       AND viewed::float / @TotalMaterials >= 0.8) AS CompletedStudents,
                COALESCE((SELECT AVG(
                     CASE WHEN @TotalMaterials > 0
                         THEN LEAST(viewed::float / @TotalMaterials * 100, 100)
                         ELSE 0
                     END
                ) FROM per_user_viewed), 0) AS AverageProgressPercent,
                (SELECT COUNT(*)
                     FROM material_views mvc
                     WHERE mvc.material_id = ANY(@MaterialIds)
                       AND mvc.is_completed = TRUE) AS MaterialViewsCompleted,
                -- Сдачи студентов курса. Исключаем синтетические submission'ы ручной
                -- приёмки staff'ом (sentinel payload) — это не реальная сдача. SELF_CHECK
                -- остаётся: самопроверка — настоящая сдача студента.
                (SELECT COUNT(*)
                     FROM issue_submissions s
                     JOIN issue_progress ips ON ips.id = s.issue_progress_id
                     JOIN course_enrollments ces ON ces.id = ips.enrollment_id
                     WHERE ces.course_id = @CourseId
                       AND s.payload <> @ManualCompletionPayload) AS IssueSubmissionsCount,
                (SELECT COUNT(DISTINCT qa.user_id)
                     FROM quiz_attempts qa
                     WHERE qa.quiz_id = ANY(@QuizIds)
                       AND qa.passed = TRUE) AS QuizPassersCount
            """;

        StatsRow row = await connection.QuerySingleAsync<StatsRow>(
            new CommandDefinition(
                sql,
                new
                {
                    query.CourseId,
                    TotalMaterials = totalMaterials,
                    MaterialIds = materialIds,
                    QuizIds = quizIds,
                    ManualCompletionPayload = MANUAL_COMPLETION_PAYLOAD,
                },
                cancellationToken: cancellationToken));

        return new CourseProgressStatsResponse(
            row.EngagedStudents,
            row.CompletedStudents,
            Math.Round(row.AverageProgressPercent, 1),
            row.MaterialViewsCompleted,
            row.IssueSubmissionsCount,
            row.QuizPassersCount);
    }

    private sealed class StatsRow
    {
        public long EngagedStudents { get; init; }

        public long CompletedStudents { get; init; }

        public double AverageProgressPercent { get; init; }

        public long MaterialViewsCompleted { get; init; }

        public long IssueSubmissionsCount { get; init; }

        public long QuizPassersCount { get; init; }
    }
}
