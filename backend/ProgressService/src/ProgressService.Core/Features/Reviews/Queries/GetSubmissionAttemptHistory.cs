using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Dtos;

namespace ProgressService.Core.Features.Reviews.Queries;

public sealed record GetSubmissionAttemptHistoryQuery(Guid SubmissionId) : IQuery;

public sealed class GetSubmissionAttemptHistoryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/reviews/submissions/{submissionId:guid}/history",
                async Task<EndpointResult<IReadOnlyList<IssueSubmissionHistoryItemDto>>> (
                        [FromRoute] Guid submissionId,
                        [FromServices] GetSubmissionAttemptHistoryHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetSubmissionAttemptHistoryQuery(submissionId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class GetSubmissionAttemptHistoryHandler
    : IQueryHandlerWithResult<IReadOnlyList<IssueSubmissionHistoryItemDto>, GetSubmissionAttemptHistoryQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetSubmissionAttemptHistoryHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<IssueSubmissionHistoryItemDto>, Error>> Handle(
        GetSubmissionAttemptHistoryQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Все попытки той же IssueProgress, что и текущая submission — это все
        // attempts одного и того же студента по одной задаче в рамках одного
        // enrollment'а. Порядок ASC: повторные отправки идут после первой.
        const string sql = """
                           SELECT
                               s.id              AS SubmissionId,
                               s.attempt_number  AS AttemptNumber,
                               s.payload         AS Payload,
                               s.review_status   AS ReviewStatus,
                               s.submitted_at    AS SubmittedAt,
                               s.review_started_at AS ReviewStartedAt,
                               s.reviewed_at     AS ReviewedAt,
                               s.feedback        AS Feedback,
                               s.latest_ai_verdict    AS LatestAiVerdict,
                               s.ai_iterations_count  AS AiIterationsCount,
                               s.last_ai_iteration_at AS LastAiIterationAt,
                               s.ai_review_status     AS AiReviewStatus,
                               s.author_help_requested_at AS AuthorHelpRequestedAt
                           FROM issue_submissions s
                           WHERE s.issue_progress_id = (
                               SELECT issue_progress_id
                               FROM issue_submissions
                               WHERE id = @SubmissionId
                           )
                           ORDER BY s.attempt_number ASC, s.submitted_at ASC;
                           """;

        CommandDefinition command = new(
            sql,
            new { SubmissionId = query.SubmissionId },
            cancellationToken: cancellationToken);

        List<IssueSubmissionHistoryItemDto> attempts =
            (await connection.QueryAsync<IssueSubmissionHistoryItemDto>(command)).ToList();

        if (attempts.Count == 0)
        {
            return GeneralErrors.NotFound(query.SubmissionId);
        }

        return Result.Success<IReadOnlyList<IssueSubmissionHistoryItemDto>, Error>(attempts);
    }
}
