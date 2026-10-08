using System.Data.Common;
using ContentAccess;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetCourseLearningStateQuery(Guid CourseId) : IQuery;

public sealed class GetCourseLearningStateQueryValidator : AbstractValidator<GetCourseLearningStateQuery>
{
    public GetCourseLearningStateQueryValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetCourseLearningStateQuery.CourseId)));
    }
}

public sealed class GetCourseLearningStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/{courseId:guid}/learning-state",
                async Task<EndpointResult<CourseLearningStateDto?>> (
                        [FromRoute] Guid courseId,
                        [FromServices] GetCourseLearningStateHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetCourseLearningStateQuery(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetCourseLearningStateHandler
    : IQueryHandlerWithResult<CourseLearningStateDto?, GetCourseLearningStateQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;
    private readonly IValidator<GetCourseLearningStateQuery> _validator;

    public GetCourseLearningStateHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData user,
        IValidator<GetCourseLearningStateQuery> validator)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _entitlementChecker = entitlementChecker;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<CourseLearningStateDto?, Error>> Handle(
        GetCourseLearningStateQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // Параллельно тащим blueprint из ECS — он отдаёт total + полный список material_id
        // (модули + Лента курса + опубликованные подборки), который ниже джойнится к material_views.
        Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> blueprintTask =
            _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([query.CourseId]),
                cancellationToken);

        // Per-enrollment агрегаты: модули и задания. materialsViewed сюда больше не входит —
        // источник правды по материалам — material_views (см. ниже), которая user-scoped.
        // Phase E (#45): type/status столбцы удалены. access-derive-model (#367): archive нет.
        const string enrollmentSql = """
                                     SELECT
                                         ce.id AS Id,
                                         ce.course_id AS CourseId,
                                         ce.enrolled_at AS EnrolledAt
                                     FROM course_enrollments ce
                                     WHERE ce.user_id = @UserId
                                       AND ce.course_id = @CourseId
                                     LIMIT 1;

                                     SELECT
                                         (SELECT COUNT(*)::integer
                                          FROM module_progress mp
                                          WHERE mp.enrollment_id = ce.id
                                            AND mp.status = 'COMPLETED') AS ModulesCompleted,
                                         (SELECT COUNT(*)::integer
                                          FROM issue_progress ip
                                          WHERE ip.enrollment_id = ce.id
                                            AND ip.status = 'COMPLETED') AS IssuesCompleted
                                     FROM course_enrollments ce
                                     WHERE ce.user_id = @UserId
                                       AND ce.course_id = @CourseId
                                     LIMIT 1;

                                     SELECT
                                         ip.issue_id AS IssueId,
                                         ip.project_id AS ProjectId,
                                         ip.status AS Status,
                                         ip.started_at AS StartedAt,
                                         ip.completed_at AS CompletedAt,
                                         submission.id AS SubmissionId,
                                         submission.payload AS Payload,
                                         submission.review_status AS ReviewStatus,
                                         submission.submitted_at AS SubmittedAt,
                                         submission.review_started_at AS ReviewStartedAt,
                                         submission.reviewed_at AS ReviewedAt,
                                         submission.feedback AS Feedback
                                     FROM issue_progress ip
                                     JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                                     LEFT JOIN LATERAL (
                                         SELECT
                                             s.id,
                                             s.payload,
                                             s.review_status,
                                             s.submitted_at,
                                             s.review_started_at,
                                             s.reviewed_at,
                                             s.feedback
                                         FROM issue_submissions s
                                         WHERE s.issue_progress_id = ip.id
                                         ORDER BY s.attempt_number DESC, s.submitted_at DESC
                                         LIMIT 1
                                     ) submission ON TRUE
                                     WHERE ce.user_id = @UserId
                                       AND ce.course_id = @CourseId;

                                     SELECT
                                         cp.entity_type AS EntityType,
                                         cp.entity_id AS EntityId,
                                         cp.opened_at AS OpenedAt
                                     FROM course_positions cp
                                     WHERE cp.user_id = @UserId
                                       AND cp.course_id = @CourseId
                                     LIMIT 1;
                                     """;

        EnrollmentRow? enrollment;
        SummaryRow summaryRow = new();
        List<IssueRow> issueRows = [];
        CoursePositionRow? positionRow = null;

        await using (SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                enrollmentSql,
                new { UserId = _user.UserId, CourseId = query.CourseId },
                cancellationToken: cancellationToken)))
        {
            enrollment = await multi.ReadFirstOrDefaultAsync<EnrollmentRow>();
            if (enrollment is not null)
            {
                summaryRow = await multi.ReadSingleAsync<SummaryRow>();
                issueRows = (await multi.ReadAsync<IssueRow>()).ToList();
                positionRow = await multi.ReadFirstOrDefaultAsync<CoursePositionRow>();
            }
        }

        if (enrollment is null)
        {
            // Derive-модель (epic access-derive-model, Phase 1): нет прогресс-якоря, но
            // пользователь может быть *entitled* (lifetime/course grant) и ещё не начинал.
            // Тогда отдаём zeroed state (0%, not started), а не null — иначе страница курса
            // у grant-holder'а выглядела бы как 404 до первого engagement (баг #366).
            // ВАЖНО: вызываем ПОСЛЕ выхода из using-блока GridReader'а — иначе второй Dapper-
            // запрос внутри BuildZeroedStateOrNull (passed-quiz lookup) падает на той же
            // connection с NpgsqlOperationInProgressException «command already in progress»
            // (баг #590: при наличии квизов в blueprint → 500 → фронт дизейблит кнопки).
            return await BuildZeroedStateOrNull(query.CourseId, blueprintTask, cancellationToken);
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult = await blueprintTask;
        if (blueprintResult.IsFailure)
        {
            return blueprintResult.Error;
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        if (blueprint is null)
        {
            return Error.NotFound(
                "course.progress.blueprint.not.found",
                $"Course progress blueprint not found for course {query.CourseId}");
        }

        Guid[] materialIds = blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray();
        List<MaterialLearningItemDto> materials = [];

        if (materialIds.Length > 0)
        {
            // is_completed=TRUE — только явно отмеченные «Изучено» (issue #285).
            // Silent track-view'ы (mount detail-страницы) НЕ считаются изученными.
            const string materialsSql = """
                                        SELECT
                                            mv.material_id AS MaterialId,
                                            'VIEWED' AS Status,
                                            COALESCE(mv.completed_at, mv.viewed_at) AS ViewedAt
                                        FROM material_views mv
                                        WHERE mv.user_id = @UserId
                                          AND mv.material_id = ANY(@MaterialIds)
                                          AND mv.is_completed = TRUE;
                                        """;

            materials = (await connection.QueryAsync<MaterialLearningItemDto>(
                new CommandDefinition(
                    materialsSql,
                    new { UserId = _user.UserId, MaterialIds = materialIds },
                    cancellationToken: cancellationToken))).ToList();
        }

        // Квизы (ST-13 #493): blueprint.TotalItems включает PUBLISHED-квизы модулей, поэтому
        // числитель «N из M элементов программы» симметрично учитывает passed-попытки.
        // User-scoped, как материалы (quiz_attempts не привязаны к enrollment'у).
        // ST-16 #495: отдаём сами id (фронту нужна галочка «пройден» на quiz-строках
        // программы), счётчик — производный от длины списка.
        List<Guid> passedQuizIds = [];
        if (blueprint.QuizIds.Count > 0)
        {
            Guid[] quizIds = blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();

            const string quizzesSql = """
                                      SELECT DISTINCT qa.quiz_id
                                      FROM quiz_attempts qa
                                      WHERE qa.user_id = @UserId
                                        AND qa.quiz_id = ANY(@QuizIds)
                                        AND qa.passed = TRUE;
                                      """;

            passedQuizIds = (await connection.QueryAsync<Guid>(
                new CommandDefinition(
                    quizzesSql,
                    new { UserId = _user.UserId, QuizIds = quizIds },
                    cancellationToken: cancellationToken))).ToList();
        }

        int quizzesPassed = passedQuizIds.Count;
        int materialsViewed = materials.Count;
        int completedItems = materialsViewed + summaryRow.IssuesCompleted + quizzesPassed;
        int progressPercent = blueprint.TotalItems == 0
            ? 0
            : (int)Math.Floor((decimal)completedItems / blueprint.TotalItems * 100);

        List<IssueLearningItemDto> issues = issueRows
            .Select(row => new IssueLearningItemDto(
                row.IssueId,
                row.ProjectId,
                row.Status,
                row.StartedAt,
                row.CompletedAt,
                row.SubmissionId is null
                    ? null
                    : new IssueLatestSubmissionDto(
                        row.SubmissionId.Value,
                        row.Payload!,
                        row.ReviewStatus!,
                        row.SubmittedAt!.Value,
                        row.ReviewStartedAt,
                        row.ReviewedAt,
                        row.Feedback)))
            .ToList();

        CoursePositionDto? lastPosition = positionRow is null
            ? null
            : new CoursePositionDto(positionRow.EntityType, positionRow.EntityId, positionRow.OpenedAt);

        return new CourseLearningStateDto(
            enrollment.Id,
            enrollment.CourseId,
            new CourseLearningSummaryDto(
                blueprint.TotalModules,
                blueprint.TotalMaterials,
                materialsViewed,
                summaryRow.ModulesCompleted,
                blueprint.TotalUniqueIssues,
                summaryRow.IssuesCompleted,
                blueprint.TotalItems,
                completedItems,
                progressPercent),
            issues,
            materials,
            enrollment.EnrolledAt,
            lastPosition,
            passedQuizIds);
    }

    private async Task<Result<CourseLearningStateDto?, Error>> BuildZeroedStateOrNull(
        Guid courseId,
        Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> blueprintTask,
        CancellationToken cancellationToken)
    {
        AccessDecision access = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.COURSE,
            courseId,
            cancellationToken);

        if (!access.IsGranted)
        {
            // Не entitled и нет прогресса — поведение как раньше (null).
            _ = await blueprintTask;
            return Result.Success<CourseLearningStateDto?, Error>(null);
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult = await blueprintTask;
        if (blueprintResult.IsFailure)
        {
            return blueprintResult.Error;
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        if (blueprint is null)
        {
            return Error.NotFound(
                "course.progress.blueprint.not.found",
                $"Course progress blueprint not found for course {courseId}");
        }

        // Passed-квизы user-scoped и не зависят от enrollment-якоря: студент мог пройти
        // квиз со standalone-страницы /quizzes/{id} ДО первого касания курса — без этого
        // запроса zeroed-state показывал бы его «не пройденным» (ревью quiz-standalone).
        List<Guid> passedQuizIds = [];
        if (blueprint.QuizIds.Count > 0)
        {
            Guid[] quizIds = blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();
            DbConnection connection = _transactionManager.GetDbConnection();
            passedQuizIds = (await connection.QueryAsync<Guid>(
                new CommandDefinition(
                    """
                    SELECT DISTINCT qa.quiz_id
                    FROM quiz_attempts qa
                    WHERE qa.user_id = @UserId
                      AND qa.quiz_id = ANY(@QuizIds)
                      AND qa.passed = TRUE;
                    """,
                    new { UserId = _user.UserId, QuizIds = quizIds },
                    cancellationToken: cancellationToken))).ToList();
        }

        int completedItems = passedQuizIds.Count;
        int progressPercent = blueprint.TotalItems == 0
            ? 0
            : (int)Math.Floor((decimal)completedItems / blueprint.TotalItems * 100);

        // Synthetic «absent enrollment» — Guid.Empty означает «нет прогресс-якоря», фронт
        // рендерит 0%/not started. EnrolledAt = default (нет реальной даты записи).
        return new CourseLearningStateDto(
            Guid.Empty,
            courseId,
            new CourseLearningSummaryDto(
                blueprint.TotalModules,
                blueprint.TotalMaterials,
                0,
                0,
                blueprint.TotalUniqueIssues,
                0,
                blueprint.TotalItems,
                completedItems,
                progressPercent),
            [],
            [],
            default,
            null,
            passedQuizIds);
    }

    private sealed class CoursePositionRow
    {
        public string EntityType { get; init; } = null!;
        public Guid EntityId { get; init; }
        public DateTime OpenedAt { get; init; }
    }

    private sealed class EnrollmentRow
    {
        public Guid Id { get; init; }
        public Guid CourseId { get; init; }
        public DateTime EnrolledAt { get; init; }
    }

    private sealed class SummaryRow
    {
        public int ModulesCompleted { get; init; }
        public int IssuesCompleted { get; init; }
    }

    private sealed class IssueRow
    {
        public Guid IssueId { get; init; }
        public Guid ProjectId { get; init; }
        public string Status { get; init; } = null!;
        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public Guid? SubmissionId { get; init; }
        public string? Payload { get; init; }
        public string? ReviewStatus { get; init; }
        public DateTime? SubmittedAt { get; init; }
        public DateTime? ReviewStartedAt { get; init; }
        public DateTime? ReviewedAt { get; init; }
        public string? Feedback { get; init; }
    }
}
