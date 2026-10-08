using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace ProgressService.Core.Features.Admin;

public sealed record GetUserSubmissionsForAdminQuery(Guid UserId) : IQuery;

public sealed record AdminUserSubmissionRow(
    Guid Id,
    Guid IssueId,
    Guid CourseId,
    string ReviewStatus,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    int AttemptNumber,
    string? LatestAiVerdict,
    int AiIterationsCount,
    string? AiReviewStatus,
    bool ReadyForHumanReview);

public sealed record AdminUserSubmissionsResponse(IReadOnlyList<AdminUserSubmissionRow> Items);

public sealed class GetUserSubmissionsForAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/admin/users/{userId:guid}/submissions",
                async Task<EndpointResult<AdminUserSubmissionsResponse>> (
                    Guid userId,
                    [FromServices] GetUserSubmissionsForAdminHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserSubmissionsForAdminQuery(userId), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUserSubmissionsForAdminHandler
    : IQueryHandlerWithResult<AdminUserSubmissionsResponse, GetUserSubmissionsForAdminQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserSubmissionsForAdminHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserSubmissionsResponse, Error>> Handle(
        GetUserSubmissionsForAdminQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // issue_id и владелец живут на issue_progress (одна строка на enrollment+issue), не на
        // submission'е; курс резолвится через enrollment. (До этого fix'а запрос ссылался на
        // несуществующие issue_progresses.user_id / issue_submissions.issue_id → таб падал 500.)
        const string sql = """
            SELECT
                s.id,
                ip.issue_id,
                ce.course_id,
                s.review_status,
                s.submitted_at,
                s.reviewed_at,
                s.attempt_number,
                s.latest_ai_verdict,
                s.ai_iterations_count,
                s.ai_review_status,
                s.ready_for_human_review
            FROM issue_submissions s
            JOIN issue_progress ip ON ip.id = s.issue_progress_id
            JOIN course_enrollments ce ON ce.id = ip.enrollment_id
            WHERE ce.user_id = @UserId
            ORDER BY s.submitted_at DESC
            LIMIT 50;
            """;

        CommandDefinition command = new(
            sql,
            new { UserId = query.UserId },
            cancellationToken: cancellationToken);

        IEnumerable<AdminUserSubmissionRow> rows =
            await connection.QueryAsync<AdminUserSubmissionRow>(command);

        return new AdminUserSubmissionsResponse(rows.ToList());
    }
}
