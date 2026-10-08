using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;

namespace ProgressService.Core.Features.Reviews.Queries;

public sealed record GetReviewedReviewIssuesQuery(GetReviewIssuesRequest Request) : IQuery;

public sealed class GetReviewedReviewIssuesQueryValidator : AbstractValidator<GetReviewedReviewIssuesQuery>
{
    public GetReviewedReviewIssuesQueryValidator()
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

public sealed class GetReviewedReviewIssuesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/reviews/issues/reviewed",
                async Task<EndpointResult<ReviewIssuesPagedResponse>> (
                    [AsParameters] GetReviewIssuesRequest request,
                    [FromServices] GetReviewedReviewIssuesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetReviewedReviewIssuesQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

/// <summary>
/// Keyset cursor for reviewed issues: (reviewed_at DESC NULLS LAST, submitted_at DESC, id DESC).
/// </summary>
public sealed record ReviewedIssueCursor(DateTime? ReviewedAt, DateTime SubmittedAt, Guid LastId)
{
    public static string Encode(DateTime? reviewedAt, DateTime submittedAt, Guid lastId)
    {
        var cursor = new ReviewedIssueCursor(reviewedAt, submittedAt, lastId);
        string json = JsonSerializer.Serialize(cursor);
        return Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static ReviewedIssueCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor));
            return JsonSerializer.Deserialize<ReviewedIssueCursor>(json);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class GetReviewedReviewIssuesHandler
    : IQueryHandlerWithResult<ReviewIssuesPagedResponse, GetReviewedReviewIssuesQuery>
{
    private readonly IValidator<GetReviewedReviewIssuesQuery> _validator;
    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IEducationContentServiceClient _ecsClient;

    public GetReviewedReviewIssuesHandler(
        IValidator<GetReviewedReviewIssuesQuery> validator,
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
        GetReviewedReviewIssuesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // #369: одна карточка на группу «студент + задание» (issue_progress).
        // Представитель = последняя попытка; reviewed таб = группы, у которых
        // последняя попытка вынесена в терминальный статус (APPROVED/CHANGES_REQUESTED).
        const string countSql = """
            SELECT COUNT(*)
            FROM (
                SELECT
                    s.id AS SubmissionId,
                    s.review_status,
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
              AND latest.review_status IN ('APPROVED', 'CHANGES_REQUESTED')
            """;

        int page = query.Request.Page;
        int pageSize = query.Request.PageSize;

        ReviewedIssueCursor? cursor = ReviewedIssueCursor.Decode(query.Request.Cursor);
        bool useCursor = cursor is not null;

        // When cursor is present, page is ignored (cursor-based keyset pagination).
        // Otherwise fall back to offset for backwards compat with the deprecated page param.
        int offset = useCursor ? 0 : (page - 1) * pageSize;

        // Keyset filter emulates (reviewed_at DESC NULLS LAST, submitted_at DESC, id DESC).
        // NULLS LAST means rows with NULL reviewed_at come AFTER non-NULLs in the sort order,
        // so a "next page" is anything ranked strictly below the cursor row.
        //   1) cursor.ReviewedAt is NOT NULL → take rows with reviewed_at < cursor, OR
        //      reviewed_at = cursor AND (submitted_at, id) < (cursor.SubmittedAt, cursor.Id), OR
        //      reviewed_at IS NULL (those are later due to NULLS LAST)
        //   2) cursor.ReviewedAt IS NULL → only rows with reviewed_at IS NULL AND
        //      (submitted_at, id) < (cursor.SubmittedAt, cursor.Id)
        // CTE `latest` — представитель группы (rn=1, max attempt_number) + attempts
        // окном. Reviewed таб = группы, у которых последняя попытка терминальна.
        // Keyset/ordering — по reviewed_at представителя (3-частный, NULLS LAST).
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
              AND ReviewStatus IN ('APPROVED', 'CHANGES_REQUESTED')
              AND (
                  @UseCursor = FALSE
                  OR (
                      @CursorReviewedAt IS NOT NULL AND (
                          ReviewedAt < @CursorReviewedAt
                          OR (
                              ReviewedAt = @CursorReviewedAt
                              AND (SubmittedAt, SubmissionId) < (@CursorSubmittedAt, @CursorId)
                          )
                          OR ReviewedAt IS NULL
                      )
                  )
                  OR (
                      @CursorReviewedAt IS NULL AND @UseCursor = TRUE
                      AND ReviewedAt IS NULL
                      AND (SubmittedAt, SubmissionId) < (@CursorSubmittedAt, @CursorId)
                  )
              )
            -- Tie-break on SubmissionId keeps ordering deterministic when two groups share the
            -- same reviewed_at/submitted_at (prevents duplicates across pages).
            ORDER BY ReviewedAt DESC NULLS LAST, SubmittedAt DESC, SubmissionId DESC
            LIMIT @Limit OFFSET @Offset
            """;

        // Always fetch +1 so we can emit a nextCursor even in legacy offset-mode requests.
        int fetchLimit = pageSize + 1;

        var parameters = new DynamicParameters();
        parameters.Add("CourseId", query.Request.CourseId, DbType.Guid);
        parameters.Add("SubmissionId", query.Request.SubmissionId, DbType.Guid);
        parameters.Add("Limit", fetchLimit);
        parameters.Add("Offset", offset);
        parameters.Add("UseCursor", useCursor, DbType.Boolean);
        // reviewed_at / submitted_at columns are `timestamp without time zone`; Npgsql refuses
        // Kind=Utc values for that mapping. Cursor JSON round-trips the Utc marker, so normalize
        // to Unspecified before binding.
        parameters.Add(
            "CursorReviewedAt",
            cursor?.ReviewedAt is { } reviewedAt ? DateTime.SpecifyKind(reviewedAt, DateTimeKind.Unspecified) : null,
            DbType.DateTime2);
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
            ? ReviewedIssueCursor.Encode(
                items[^1].ReviewedAt is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null,
                DateTime.SpecifyKind(items[^1].SubmittedAt, DateTimeKind.Utc),
                items[^1].SubmissionId)
            : null;

        return new ReviewIssuesPagedResponse(items, nextCursor, totalCount, page, pageSize, totalPages);
    }
}
