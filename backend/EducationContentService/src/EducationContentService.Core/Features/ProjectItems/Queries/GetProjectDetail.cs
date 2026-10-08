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

namespace EducationContentService.Core.Features.ProjectItems.Queries;

public sealed record GetProjectDetailQuery(Guid ProjectId) : IQuery;

public sealed class GetProjectDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("projects/{projectId:guid}/detail", async Task<EndpointResult<ProjectDetailDto>> (
            [FromRoute] Guid projectId,
            [FromServices] GetProjectDetailHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetProjectDetailQuery(projectId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class GetProjectDetailHandler : IQueryHandlerWithResult<ProjectDetailDto, GetProjectDetailQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetProjectDetailHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ProjectDetailDto, Error>> Handle(
        GetProjectDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                p.id,
                p.author_id,
                p.title,
                p.description,
                p.detailed_description,
                p.status,
                COALESCE(prc.requires_github_connection, TRUE) AS requires_github_connection,
                COALESCE(prc.requires_review_app, TRUE) AS requires_review_app,
                COALESCE(prc.is_auto_review_enabled, TRUE) AS is_auto_review_enabled,
                p.created_at,
                p.updated_at
            FROM projects p
            LEFT JOIN project_review_contexts prc ON prc.project_id = p.id
            WHERE p.id = @ProjectId;

            SELECT
                pi.id,
                pi.issue_id,
                pi.sort_key,
                pi.is_optional,
                pi.max_score,
                i.title AS title,
                i.status AS status,
                i.access_type AS access_type,
                i.submission_mode AS submission_mode,
                i.self_check_instructions AS self_check_instructions
            FROM project_items pi
            LEFT JOIN issues i ON pi.issue_id = i.id
            WHERE pi.project_id = @ProjectId
            ORDER BY pi.sort_key;
            """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(sql, new { query.ProjectId });

        ProjectDetailRow? projectRow = await multi.ReadFirstOrDefaultAsync<ProjectDetailRow>();
        if (projectRow == null)
            return GeneralErrors.NotFound(query.ProjectId);

        IEnumerable<ProjectItemRow> itemRows = await multi.ReadAsync<ProjectItemRow>();

        List<ProjectItemDto> items = itemRows.Select(i => new ProjectItemDto(
            i.Id,
            i.IssueId,
            i.SortKey,
            i.IsOptional,
            i.MaxScore,
            i.Title,
            i.Status,
            i.AccessType,
            i.SubmissionMode ?? "PULL_REQUEST",
            i.SelfCheckInstructions)).ToList();

        return new ProjectDetailDto(
            projectRow.Id,
            projectRow.AuthorId,
            projectRow.Title,
            projectRow.Description,
            projectRow.DetailedDescription,
            projectRow.Status,
            projectRow.CreatedAt,
            projectRow.UpdatedAt,
            projectRow.RequiresGithubConnection,
            projectRow.RequiresReviewApp,
            projectRow.IsAutoReviewEnabled,
            items);
    }

    private sealed class ProjectDetailRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string? DetailedDescription { get; init; }
        public string Status { get; init; } = null!;
        public bool RequiresGithubConnection { get; init; } = true;
        public bool RequiresReviewApp { get; init; } = true;
        public bool IsAutoReviewEnabled { get; init; } = true;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class ProjectItemRow
    {
        public Guid Id { get; init; }
        public Guid IssueId { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public int? MaxScore { get; init; }
        public string? Title { get; init; }
        public string? Status { get; init; }
        public string? AccessType { get; init; }
        public string? SubmissionMode { get; init; }
        public string? SelfCheckInstructions { get; init; }
    }
}
