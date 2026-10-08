using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

public sealed record GetIssueLookupQuery(Guid ProjectId, Guid IssueId) : IQuery;

public sealed class GetIssueLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/projects/{projectId:guid}/issues/{issueId:guid}",
            async Task<EndpointResult<IssueDto>> (
                [FromRoute] Guid projectId,
                [FromRoute] Guid issueId,
                [FromServices] GetIssueLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetIssueLookupQuery(projectId, issueId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetIssueLookupHandler : IQueryHandlerWithResult<IssueDto, GetIssueLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetIssueLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IssueDto, Error>> Handle(
        GetIssueLookupQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Validate issue exists and belongs to the project via ProjectItem.
        // Also find the ModuleId if the issue is attached to a module via ModuleItem.
        const string sql = """
            SELECT
                mi.module_id,
                i.submission_mode,
                i.self_check_instructions,
                COALESCE(prc.requires_github_connection, TRUE) AS requires_github_connection,
                COALESCE(prc.requires_review_app, TRUE) AS requires_review_app,
                (
                    COALESCE(prc.is_auto_review_enabled, TRUE)
                    AND COALESCE(rs.is_auto_review_enabled, TRUE)
                    AND i.submission_mode = 'PULL_REQUEST'
                ) AS is_auto_review_enabled
            FROM issues i
            INNER JOIN project_items pit
                ON pit.issue_id = i.id
            LEFT JOIN module_items mi
                ON mi.reference_id = i.id AND mi.item_type = 'Issue'
            LEFT JOIN project_review_contexts prc
                ON prc.project_id = pit.project_id
            LEFT JOIN review_specs rs
                ON rs.issue_id = i.id
            WHERE i.id = @IssueId
              AND pit.project_id = @ProjectId;
            """;

        IssueLookupRow? row = await connection.QueryFirstOrDefaultAsync<IssueLookupRow>(sql, new
        {
            query.IssueId,
            query.ProjectId
        });

        if (row is null)
            return GeneralErrors.NotFound(query.IssueId);

        return new IssueDto(
            row.ModuleId,
            row.SubmissionMode,
            row.SelfCheckInstructions,
            row.RequiresGithubConnection,
            row.RequiresReviewApp,
            row.IsAutoReviewEnabled);
    }

    private sealed class IssueLookupRow
    {
        public Guid? ModuleId { get; init; }
        public string SubmissionMode { get; init; } = "PULL_REQUEST";
        public string? SelfCheckInstructions { get; init; }
        public bool RequiresGithubConnection { get; init; } = true;
        public bool RequiresReviewApp { get; init; } = true;
        public bool IsAutoReviewEnabled { get; init; } = true;
    }
}
