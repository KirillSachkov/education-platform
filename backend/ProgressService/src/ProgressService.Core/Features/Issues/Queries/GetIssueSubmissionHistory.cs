using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain.Issues;

namespace ProgressService.Core.Features.Issues.Queries;

public sealed record GetIssueSubmissionHistoryQuery(Guid CourseId, Guid IssueId) : IQuery;

public sealed class GetIssueSubmissionHistoryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/{courseId:guid}/issues/{issueId:guid}/history",
                async Task<EndpointResult<IssueSubmissionHistoryDto>> (
                        [FromRoute] Guid courseId,
                        [FromRoute] Guid issueId,
                        [FromServices] GetIssueSubmissionHistoryHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetIssueSubmissionHistoryQuery(courseId, issueId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetIssueSubmissionHistoryHandler
    : IQueryHandlerWithResult<IssueSubmissionHistoryDto, GetIssueSubmissionHistoryQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetIssueSubmissionHistoryHandler(
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<IssueSubmissionHistoryDto, Error>> Handle(
        GetIssueSubmissionHistoryQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               ce.id AS EnrollmentId
                           FROM course_enrollments ce
                           WHERE ce.user_id = @UserId
                             AND ce.course_id = @CourseId
                           LIMIT 1;

                           SELECT
                               ip.id AS IssueProgressId,
                               ip.status AS CurrentStatus,
                               ip.started_at AS StartedAt,
                               ip.completed_at AS CompletedAt
                           FROM issue_progress ip
                           JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                           WHERE ce.user_id = @UserId
                             AND ce.course_id = @CourseId
                             AND ip.issue_id = @IssueId
                           LIMIT 1;

                           SELECT
                               s.id AS SubmissionId,
                               s.attempt_number AS AttemptNumber,
                               s.payload AS Payload,
                               s.review_status AS ReviewStatus,
                               s.submitted_at AS SubmittedAt,
                               s.review_started_at AS ReviewStartedAt,
                               s.reviewed_at AS ReviewedAt,
                               s.feedback AS Feedback,
                               s.latest_ai_verdict AS LatestAiVerdict,
                               s.ai_iterations_count AS AiIterationsCount,
                               s.last_ai_iteration_at AS LastAiIterationAt,
                               s.ai_review_status AS AiReviewStatus,
                               s.author_help_requested_at AS AuthorHelpRequestedAt
                           FROM issue_submissions s
                           JOIN issue_progress ip ON ip.id = s.issue_progress_id
                           JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                           WHERE ce.user_id = @UserId
                             AND ce.course_id = @CourseId
                             AND ip.issue_id = @IssueId
                           ORDER BY s.attempt_number ASC, s.submitted_at ASC;
                           """;

        CommandDefinition command = new(
            sql,
            new { UserId = _user.UserId, CourseId = query.CourseId, IssueId = query.IssueId },
            cancellationToken: cancellationToken);

        using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(command);

        EnrollmentRow? enrollment = await multi.ReadFirstOrDefaultAsync<EnrollmentRow>();

        if (enrollment is null)
        {
            return new IssueSubmissionHistoryDto(
                query.IssueId,
                IssueProgressStatus.NOT_STARTED.ToString(),
                null,
                null,
                []);
        }

        IssueProgressRow? issueProgress = await multi.ReadFirstOrDefaultAsync<IssueProgressRow>();

        if (issueProgress is null)
        {
            return new IssueSubmissionHistoryDto(
                query.IssueId,
                IssueProgressStatus.NOT_STARTED.ToString(),
                null,
                null,
                []);
        }

        List<IssueSubmissionHistoryItemDto> attempts =
            (await multi.ReadAsync<IssueSubmissionHistoryItemDto>()).ToList();

        return Result.Success<IssueSubmissionHistoryDto, Error>(
            new IssueSubmissionHistoryDto(
                query.IssueId,
                issueProgress.CurrentStatus,
                issueProgress.StartedAt,
                issueProgress.CompletedAt,
                attempts));
    }

    private sealed class EnrollmentRow
    {
        public Guid EnrollmentId { get; init; }
    }

    private sealed class IssueProgressRow
    {
        public Guid IssueProgressId { get; init; }
        public string CurrentStatus { get; init; } = null!;
        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }
}
