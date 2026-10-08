using System.Data;
using System.Data.Common;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.Reviews.Queries;

public sealed record GetInReviewIssuesQuery(GetReviewIssuesRequest Request) : IQuery;

public sealed class GetInReviewIssuesQueryValidator : AbstractValidator<GetInReviewIssuesQuery>
{
    public GetInReviewIssuesQueryValidator()
    {
        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(GeneralErrors.ValueIsInvalid("page"));

        RuleFor(x => x.Request.PageSize)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .WithError(GeneralErrors.ValueIsInvalid("pageSize"));
    }
}

public sealed class GetInReviewIssuesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/reviews/issues/in-review",
                async Task<EndpointResult<ReviewIssuesPagedResponse>> (
                    [AsParameters] GetReviewIssuesRequest request,
                    [FromServices] GetInReviewIssuesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetInReviewIssuesQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

/// <summary>
/// Author/admin oversight feed (#363): submissions currently inside the AI-review
/// pipeline, i.e. gated out of the human queue. These are PENDING attempts where
/// <c>ready_for_human_review = FALSE</c> — the ARS gate event flips the flag to false
/// when an AI review is queued, and the auto-verdict gate moves the attempt to
/// APPROVED/CHANGES_REQUESTED (→ "reviewed" tab) once a terminal verdict lands, or
/// finalizes it back to <c>true</c> (→ "pending" tab) when the AI fails. So this query
/// surfaces exactly the in-flight set the author otherwise never sees. Read-only.
/// Reuses <see cref="PendingReviewCursor"/> — identical (submitted_at DESC, id DESC) ordering.
/// </summary>
public sealed class GetInReviewIssuesHandler
    : IQueryHandlerWithResult<ReviewIssuesPagedResponse, GetInReviewIssuesQuery>
{
    private readonly IValidator<GetInReviewIssuesQuery> _validator;
    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IEducationContentServiceClient _ecsClient;

    public GetInReviewIssuesHandler(
        IValidator<GetInReviewIssuesQuery> validator,
        ITransactionManager transactionManager,
        IAuthServiceClient authServiceClient,
        IEducationContentServiceClient ecsClient)
    {
        _validator = validator;
        _transactionManager = transactionManager;
        _authServiceClient = authServiceClient;
        _ecsClient = ecsClient;
    }

    public async Task<Result<ReviewIssuesPagedResponse, Error>> Handle(
        GetInReviewIssuesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // In-flight set: PENDING attempts gated out of the human queue while AI reviews them.
        // Covered by the composite index ix_issue_submissions_ready_for_review_submitted_at.
        // #369: одна карточка на группу «студент + задание» (issue_progress).
        // Представитель = последняя попытка; in-flight таб = группы, где последняя
        // попытка PENDING и ещё гейтнута AI-пайплайном (ready_for_human_review=FALSE).
        const string countSql = """
            SELECT COUNT(*)
            FROM (
                SELECT
                    s.id AS SubmissionId,
                    s.review_status,
                    s.ready_for_human_review,
                    ROW_NUMBER() OVER (
                        PARTITION BY s.issue_progress_id
                        ORDER BY s.attempt_number DESC) AS rn
                FROM issue_submissions s
                INNER JOIN issue_progress ip ON ip.id = s.issue_progress_id
                INNER JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                WHERE (@CourseId IS NULL OR ce.course_id = @CourseId)
                  AND (@SubmissionId IS NULL OR s.issue_progress_id = (
                      SELECT target.issue_progress_id
                      FROM issue_submissions target
                      WHERE target.id = @SubmissionId))
            ) latest
            WHERE latest.rn = 1
              AND latest.review_status = 'PENDING'
              AND latest.ready_for_human_review = FALSE
            """;

        int page = query.Request.Page;
        int pageSize = query.Request.PageSize;

        PendingReviewCursor? cursor = PendingReviewCursor.Decode(query.Request.Cursor);
        bool useCursor = cursor is not null;

        // When cursor is present, page is ignored (cursor-based keyset pagination).
        // Otherwise fall back to offset for backwards compat with the deprecated page param.
        int offset = useCursor ? 0 : (page - 1) * pageSize;

        // CTE `latest` — представитель группы (rn=1) + attempts окном. In-flight
        // таб = группы, где последняя попытка ещё внутри AI-пайплайна. Keyset — по
        // представителю (тот же PendingReviewCursor).
        const string dataSql = """
            WITH latest AS (
                SELECT
                    s.id AS SubmissionId,
                    ce.course_id AS CourseId,
                    ip.project_id AS ProjectId,
                    ip.issue_id AS IssueId,
                    ce.user_id AS StudentId,
                    s.attempt_number AS SubmissionNo,
                    s.payload AS Payload,
                    ip.status AS IssueProgressStatus,
                    s.review_status AS ReviewStatus,
                    s.reviewer_id AS ReviewerId,
                    s.submitted_at AS SubmittedAt,
                    s.review_started_at AS ReviewStartedAt,
                    s.reviewed_at AS ReviewedAt,
                    s.feedback AS Feedback,
                    s.latest_ai_verdict    AS LatestAiVerdict,
                    s.ai_iterations_count  AS AiIterationsCount,
                    s.last_ai_iteration_at AS LastAiIterationAt,
                    s.ai_review_status     AS AiReviewStatus,
                    s.author_help_requested_at AS AuthorHelpRequestedAt,
                    s.author_help_message AS AuthorHelpMessage,
                    s.student_question_at AS StudentQuestionAt,
                    s.ready_for_human_review AS ReadyForHumanReview,
                    ROW_NUMBER() OVER (
                        PARTITION BY s.issue_progress_id
                        ORDER BY s.attempt_number DESC) AS rn,
                    CAST(COUNT(*) OVER (PARTITION BY s.issue_progress_id) AS int) AS AttemptsCount
                FROM issue_submissions s
                INNER JOIN issue_progress ip ON ip.id = s.issue_progress_id
                INNER JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                WHERE (@CourseId IS NULL OR ce.course_id = @CourseId)
                  AND (@SubmissionId IS NULL OR s.issue_progress_id = (
                      SELECT target.issue_progress_id
                      FROM issue_submissions target
                      WHERE target.id = @SubmissionId))
            )
            SELECT
                SubmissionId, CourseId, ProjectId, IssueId, StudentId, SubmissionNo,
                Payload, IssueProgressStatus, ReviewStatus, ReviewerId, SubmittedAt, ReviewStartedAt,
                ReviewedAt, Feedback, LatestAiVerdict, AiIterationsCount,
                LastAiIterationAt, AiReviewStatus, AuthorHelpRequestedAt, AuthorHelpMessage,
                StudentQuestionAt, AttemptsCount
            FROM latest
            WHERE rn = 1
              AND ReviewStatus = 'PENDING'
              AND ReadyForHumanReview = FALSE
              AND (
                  @CursorSubmittedAt IS NULL
                  OR (SubmittedAt, SubmissionId) < (@CursorSubmittedAt, @CursorId)
              )
            -- Tie-break on SubmissionId keeps ordering deterministic when two groups share the
            -- same representative submitted_at (prevents duplicates across pages).
            ORDER BY SubmittedAt DESC, SubmissionId DESC
            LIMIT @Limit OFFSET @Offset
            """;

        // Always fetch +1 so we can emit a nextCursor even in legacy offset-mode requests.
        int fetchLimit = pageSize + 1;

        var parameters = new DynamicParameters();
        parameters.Add("CourseId", query.Request.CourseId, DbType.Guid);
        parameters.Add("SubmissionId", query.Request.SubmissionId, DbType.Guid);
        parameters.Add("Limit", fetchLimit);
        parameters.Add("Offset", offset);
        // submitted_at column is `timestamp without time zone`; Npgsql refuses Kind=Utc values
        // for that mapping. Cursor JSON round-trips the Utc marker, so normalize to Unspecified.
        parameters.Add(
            "CursorSubmittedAt",
            cursor is null ? null : DateTime.SpecifyKind(cursor.SubmittedAt, DateTimeKind.Unspecified),
            DbType.DateTime2);
        parameters.Add("CursorId", cursor?.LastId, DbType.Guid);

        CommandDefinition command = new(
            $"{countSql};{dataSql}",
            parameters,
            cancellationToken: cancellationToken);
        using var multi = await connection.QueryMultipleAsync(command);
        int totalCount = await multi.ReadSingleAsync<int>();
        List<ReviewIssueItemDto> items = (await multi.ReadAsync<ReviewIssueItemDto>()).ToList();

        bool hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling((double)totalCount / pageSize);

        items = await ReviewUserEnricher.EnrichWithUserInfoAsync(
            items, _authServiceClient, _ecsClient, cancellationToken);

        string? nextCursor = hasMore && items.Count > 0
            ? PendingReviewCursor.Encode(
                DateTime.SpecifyKind(items[^1].SubmittedAt, DateTimeKind.Utc),
                items[^1].SubmissionId)
            : null;

        return new ReviewIssuesPagedResponse(items, nextCursor, totalCount, page, pageSize, totalPages);
    }
}
