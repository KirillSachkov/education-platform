using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases.ReviewConfig;

public sealed record GetCourseReviewCoverageQuery(Guid CourseId) : IQuery;

public sealed class GetCourseReviewCoverageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/review-coverage",
                async Task<EndpointResult<CourseReviewCoverageDto>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseReviewCoverageHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetCourseReviewCoverageQuery(courseId), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

/// <summary>
///     Course-level сводка покрытия AI-review промптами одним read-only Dapper-вызовом:
///     все проекты курса + per-issue review-spec статус (есть/нет, длины,
///     isAutoReviewEnabled) + rollup. Course-level follow-up к
///     <see cref="GetProjectReviewCoverageHandler"/> (issue #356): чтобы массово
///     заполнять промпты на весь курс, не дёргая project-coverage по каждому проекту.
///     Учитывает задачи, размещённые и в project_items, и в module_items (item_type=Issue) —
///     project-level guidelines применяются к ревью задачи независимо от места её привязки.
/// </summary>
public sealed class GetCourseReviewCoverageHandler
    : IQueryHandlerWithResult<CourseReviewCoverageDto, GetCourseReviewCoverageQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetCourseReviewCoverageHandler(ITransactionManager transactionManager, UserScopedData user)
    {
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<CourseReviewCoverageDto, Error>> Handle(
        GetCourseReviewCoverageQuery query, CancellationToken ct)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Step 1: course + author. Authorize BEFORE the heavier project/issue scan
        // (auth-before-work, mirrors GetProjectReviewCoverage).
        const string courseSql = """
            SELECT c.id, c.author_id, c.title
            FROM courses c
            WHERE c.id = @CourseId;
            """;

        CourseRow? courseRow = await connection.QueryFirstOrDefaultAsync<CourseRow>(
            new CommandDefinition(courseSql, new { query.CourseId }, cancellationToken: ct));
        if (courseRow == null)
            return GeneralErrors.NotFound(query.CourseId);

        UnitResult<Error> ownership = _user.CheckOwnership(courseRow.AuthorId);
        if (ownership.IsFailure) return ownership.Error;

        // Step 2: projects relevant to the course (attached as course_items OR owning an
        // issue placed in one of the course's modules) + their review-context, then every
        // issue in the course (both placements) + its review-spec — in one round-trip.
        const string coverageSql = """
            WITH course_project_ids AS (
                SELECT ci.reference_id AS project_id
                FROM course_items ci
                WHERE ci.course_id = @CourseId AND ci.item_type = 'Project'
                UNION
                SELECT i.project_id
                FROM module_items mi
                JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                JOIN issues i ON i.id = mi.reference_id
                WHERE ci.course_id = @CourseId AND mi.item_type = 'Issue'
            )
            SELECT
                p.id,
                p.title,
                (prc.project_id IS NOT NULL) AS has_context,
                COALESCE(length(prc.guidelines_markdown), 0) AS guidelines_length,
                prc.is_auto_review_enabled AS context_is_auto_review_enabled,
                prc.updated_at AS context_updated_at
            FROM course_project_ids cpi
            JOIN projects p ON p.id = cpi.project_id
            LEFT JOIN project_review_contexts prc ON prc.project_id = p.id
            ORDER BY p.title;

            WITH course_issue_ids AS (
                SELECT pi.issue_id
                FROM project_items pi
                JOIN course_items ci ON ci.reference_id = pi.project_id AND ci.item_type = 'Project'
                WHERE ci.course_id = @CourseId
                UNION
                SELECT mi.reference_id AS issue_id
                FROM module_items mi
                JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                WHERE ci.course_id = @CourseId AND mi.item_type = 'Issue'
            )
            SELECT
                i.id AS issue_id,
                i.project_id AS project_id,
                i.title AS title,
                i.status AS status,
                (rs.issue_id IS NOT NULL) AS has_review_spec,
                COALESCE(length(rs.author_prompt), 0) AS author_prompt_length,
                COALESCE(length(rs.review_aspects), 0) AS review_aspects_length,
                rs.is_auto_review_enabled AS spec_is_auto_review_enabled,
                rs.updated_at AS spec_updated_at
            FROM course_issue_ids cii
            JOIN issues i ON i.id = cii.issue_id
            LEFT JOIN review_specs rs ON rs.issue_id = i.id
            ORDER BY i.project_id, i.title;
            """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(coverageSql, new { query.CourseId }, cancellationToken: ct));

        List<ProjectRow> projectRows = (await multi.ReadAsync<ProjectRow>()).ToList();
        List<IssueRow> issueRows = (await multi.ReadAsync<IssueRow>()).ToList();

        Dictionary<Guid, List<IssueReviewCoverageDto>> issuesByProject = issueRows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new IssueReviewCoverageDto(
                    r.IssueId,
                    r.Title,
                    r.Status,
                    r.HasReviewSpec,
                    r.AuthorPromptLength,
                    r.ReviewAspectsLength,
                    r.SpecIsAutoReviewEnabled ?? true,
                    r.SpecUpdatedAt)).ToList());

        List<ProjectReviewCoverageDto> projects = projectRows.Select(p => new ProjectReviewCoverageDto(
            p.Id,
            p.Title,
            p.HasContext,
            p.ContextIsAutoReviewEnabled ?? true,
            p.GuidelinesLength,
            p.ContextUpdatedAt,
            issuesByProject.GetValueOrDefault(p.Id, []))).ToList();

        List<IssueReviewCoverageDto> allIssues = projects.SelectMany(p => p.Issues).ToList();

        CourseReviewCoverageSummaryDto summary = new(
            ProjectCount: projects.Count,
            ProjectsWithContext: projects.Count(p => p.HasProjectContext),
            ProjectsAutoReviewDisabled: projects.Count(p => !p.ProjectIsAutoReviewEnabled),
            IssueCount: allIssues.Count,
            IssuesWithReviewSpec: allIssues.Count(i => i.HasReviewSpec),
            IssuesWithAuthorPrompt: allIssues.Count(i => i.AuthorPromptLength > 0),
            IssuesWithReviewAspects: allIssues.Count(i => i.ReviewAspectsLength > 0),
            IssuesAutoReviewDisabled: allIssues.Count(i => !i.IsAutoReviewEnabled));

        return new CourseReviewCoverageDto(courseRow.Id, courseRow.Title, summary, projects);
    }

    private sealed class CourseRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
    }

    private sealed class ProjectRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = null!;
        public bool HasContext { get; init; }
        public int GuidelinesLength { get; init; }
        public bool? ContextIsAutoReviewEnabled { get; init; }
        public DateTime? ContextUpdatedAt { get; init; }
    }

    private sealed class IssueRow
    {
        public Guid IssueId { get; init; }
        public Guid ProjectId { get; init; }
        public string? Title { get; init; }
        public string? Status { get; init; }
        public bool HasReviewSpec { get; init; }
        public int AuthorPromptLength { get; init; }
        public int ReviewAspectsLength { get; init; }
        public bool? SpecIsAutoReviewEnabled { get; init; }
        public DateTime? SpecUpdatedAt { get; init; }
    }
}
