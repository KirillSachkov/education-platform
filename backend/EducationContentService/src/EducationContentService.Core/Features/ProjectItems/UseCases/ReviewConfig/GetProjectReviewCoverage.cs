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

public sealed record GetProjectReviewCoverageQuery(Guid ProjectId) : IQuery;

public sealed class GetProjectReviewCoverageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("projects/{projectId:guid}/review-coverage",
                async Task<EndpointResult<ProjectReviewCoverageDto>> (
                    [FromRoute] Guid projectId,
                    [FromServices] GetProjectReviewCoverageHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetProjectReviewCoverageQuery(projectId), ct))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

/// <summary>
///     Сводка покрытия AI-review промптами по проекту: PROJECT-level guidelines
///     + per-issue review-spec статус (есть/нет, длины, isAutoReviewEnabled) одним
///     read-only Dapper-запросом. Длины считаются в SQL (<c>length()</c>), чтобы не
///     тащить 50k markdown ради измерения. Нужно для массового заполнения промптов
///     через MCP — одним вызовом видно, у каких задач spec ещё пуст. Issue #356.
/// </summary>
public sealed class GetProjectReviewCoverageHandler
    : IQueryHandlerWithResult<ProjectReviewCoverageDto, GetProjectReviewCoverageQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetProjectReviewCoverageHandler(ITransactionManager transactionManager, UserScopedData user)
    {
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<ProjectReviewCoverageDto, Error>> Handle(
        GetProjectReviewCoverageQuery query, CancellationToken ct)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Step 1: project + its review-context. Authorize on the project's author
        // BEFORE reading the per-issue rows, so a non-owner never causes the heavier
        // issue scan to run (auth-before-work, mirrors GetReviewSpec/GetProjectReviewContext).
        const string projectSql = """
            SELECT
                p.id,
                p.author_id,
                p.title,
                (prc.project_id IS NOT NULL) AS has_context,
                COALESCE(length(prc.guidelines_markdown), 0) AS guidelines_length,
                prc.is_auto_review_enabled AS context_is_auto_review_enabled,
                prc.updated_at AS context_updated_at
            FROM projects p
            LEFT JOIN project_review_contexts prc ON prc.project_id = p.id
            WHERE p.id = @ProjectId;
            """;

        ProjectRow? projectRow =
            await connection.QueryFirstOrDefaultAsync<ProjectRow>(projectSql, new { query.ProjectId });
        if (projectRow == null)
            return GeneralErrors.NotFound(query.ProjectId);

        UnitResult<Error> ownership = _user.CheckOwnership(projectRow.AuthorId);
        if (ownership.IsFailure) return ownership.Error;

        // Step 2: per-issue review-spec status, ordered by project position.
        const string issuesSql = """
            SELECT
                pi.issue_id,
                i.title AS title,
                i.status AS status,
                (rs.issue_id IS NOT NULL) AS has_review_spec,
                COALESCE(length(rs.author_prompt), 0) AS author_prompt_length,
                COALESCE(length(rs.review_aspects), 0) AS review_aspects_length,
                rs.is_auto_review_enabled AS spec_is_auto_review_enabled,
                rs.updated_at AS spec_updated_at
            FROM project_items pi
            LEFT JOIN issues i ON pi.issue_id = i.id
            LEFT JOIN review_specs rs ON rs.issue_id = pi.issue_id
            WHERE pi.project_id = @ProjectId
            ORDER BY pi.sort_key;
            """;

        IEnumerable<IssueRow> issueRows =
            await connection.QueryAsync<IssueRow>(issuesSql, new { query.ProjectId });

        List<IssueReviewCoverageDto> issues = issueRows.Select(r => new IssueReviewCoverageDto(
            r.IssueId,
            r.Title,
            r.Status,
            r.HasReviewSpec,
            r.AuthorPromptLength,
            r.ReviewAspectsLength,
            r.SpecIsAutoReviewEnabled ?? true,
            r.SpecUpdatedAt)).ToList();

        return new ProjectReviewCoverageDto(
            projectRow.Id,
            projectRow.Title,
            projectRow.HasContext,
            projectRow.ContextIsAutoReviewEnabled ?? true,
            projectRow.GuidelinesLength,
            projectRow.ContextUpdatedAt,
            issues);
    }

    private sealed class ProjectRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public bool HasContext { get; init; }
        public int GuidelinesLength { get; init; }
        public bool? ContextIsAutoReviewEnabled { get; init; }
        public DateTime? ContextUpdatedAt { get; init; }
    }

    private sealed class IssueRow
    {
        public Guid IssueId { get; init; }
        public string? Title { get; init; }
        public string? Status { get; init; }
        public bool HasReviewSpec { get; init; }
        public int AuthorPromptLength { get; init; }
        public int ReviewAspectsLength { get; init; }
        public bool? SpecIsAutoReviewEnabled { get; init; }
        public DateTime? SpecUpdatedAt { get; init; }
    }
}
