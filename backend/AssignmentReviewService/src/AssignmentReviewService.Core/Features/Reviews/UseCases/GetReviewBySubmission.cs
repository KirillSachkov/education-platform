using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Domain.Reviews;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

public sealed record GetReviewBySubmissionQuery(Guid SubmissionId) : IQuery;

public sealed class GetReviewBySubmissionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/assignment-review/reviews/by-submission/{submissionId:guid}/",
                async Task<EndpointResult<AiReviewDetailDto>> (
                    [FromRoute] Guid submissionId,
                    [FromServices] GetReviewBySubmissionHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetReviewBySubmissionQuery(submissionId), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     Возвращает <see cref="AiReviewDetailDto"/> для submission'а текущего юзера
///     (или admin'а). Если AiReview не существует — 404 (фронт показывает «AI ещё
///     не запускалась» / «GitHub App не подключен» case'ы по контексту).
/// </summary>
public sealed class GetReviewBySubmissionHandler
    : IQueryHandlerWithResult<AiReviewDetailDto, GetReviewBySubmissionQuery>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IStudentPrMessagesRepository _messages;
    private readonly UserScopedData _user;

    public GetReviewBySubmissionHandler(
        IAiReviewsRepository reviews,
        IStudentPrMessagesRepository messages,
        UserScopedData user)
    {
        _reviews = reviews;
        _messages = messages;
        _user = user;
    }

    public async Task<Result<AiReviewDetailDto, Error>> Handle(
        GetReviewBySubmissionQuery query, CancellationToken ct)
    {
        AiReview? review = await _reviews.GetByAsync(r => r.SubmissionId == query.SubmissionId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(query.SubmissionId);

        if (!_user.IsOwnerOrAdmin(review.UserId))
            return ReviewErrors.AccessDenied();

        IReadOnlyList<AiReviewIterationDto> iterationDtos = review.Iterations
            .OrderBy(i => i.IterationNumber)
            .Select(i => new AiReviewIterationDto(
                i.Id,
                i.IterationNumber,
                i.CommitSha,
                i.Status.ToString(),
                i.Verdict?.ToString(),
                i.Summary,
                i.InlineCommentsCount,
                i.GitHubReviewId,
                i.ModelUsed,
                i.InputTokens,
                i.OutputTokens,
                new DateTimeOffset(i.StartedAt.UtcDateTime, TimeSpan.Zero),
                i.CompletedAt is null ? null : new DateTimeOffset(i.CompletedAt.Value.UtcDateTime, TimeSpan.Zero),
                i.FailureReason,
                i.RequestedFiles,
                i.ContextRounds))
            .ToList();

        // #713 — обратный канал: комментарии студента в PR, по возрастанию createdAt.
        IReadOnlyList<Domain.Reviews.StudentPrMessage> messages =
            await _messages.GetByAiReviewIdAsync(review.Id, ct);
        IReadOnlyList<StudentPrMessageDto> messageDtos = messages
            .Select(m => new StudentPrMessageDto(
                m.Id,
                m.GitHubCommentId,
                m.InReplyToGitHubId,
                m.AuthorGithubLogin,
                m.Body,
                m.Path,
                m.Line,
                m.CommentUrl,
                new DateTimeOffset(m.CreatedAtGithub.UtcDateTime, TimeSpan.Zero),
                m.AnsweredAt is null ? null : new DateTimeOffset(m.AnsweredAt.Value.UtcDateTime, TimeSpan.Zero),
                m.AnswerBody))
            .ToList();

        return new AiReviewDetailDto(
            review.Id,
            review.SubmissionId,
            review.IssueId,
            review.UserId,
            review.Provider.ToString(),
            review.RepoFullName,
            review.PullNumber,
            review.PullRequestUrl,
            review.Status.ToString(),
            review.LatestVerdict?.ToString(),
            review.IterationsCount,
            review.CreatedAt,
            review.UpdatedAt,
            iterationDtos,
            messageDtos);
    }
}
