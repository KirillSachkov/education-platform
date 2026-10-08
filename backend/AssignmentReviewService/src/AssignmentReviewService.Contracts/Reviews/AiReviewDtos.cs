namespace AssignmentReviewService.Contracts.Reviews;

/// <summary>
///     Phase 9 (#15) — DTO студенту/автору. Несёт всю необходимую инфу для UI:
///     header (status + repo + PR link), denorm latest verdict, список iteration'ов и —
///     обратный канал (#713) — комментарии студента в PR (<see cref="StudentMessages"/>).
/// </summary>
public sealed record AiReviewDetailDto(
    Guid Id,
    Guid SubmissionId,
    Guid IssueId,
    Guid UserId,
    string Provider,
    string RepoFullName,
    int PullNumber,
    string PullRequestUrl,
    string Status,
    string? LatestVerdict,
    int IterationsCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AiReviewIterationDto> Iterations,
    IReadOnlyList<StudentPrMessageDto> StudentMessages);

public sealed record AiReviewIterationDto(
    Guid Id,
    int IterationNumber,
    string CommitSha,
    string Status,
    string? Verdict,
    string Summary,
    int InlineCommentsCount,
    long? GitHubReviewId,
    string ModelUsed,
    int? InputTokens,
    int? OutputTokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    /// <summary>Файлы репо, дозапрошенные моделью через need_files (#798); null = дозапроса не было.</summary>
    IReadOnlyList<string>? RequestedFiles = null,
    /// <summary>Дополнительные LLM-раунды цикла дозапроса (#798); 0 = одношот.</summary>
    int ContextRounds = 0);

/// <summary>
///     Issue #713 — комментарий студента в его PR (обратный канал к AI-ревью), плюс ответ
///     автора (<see cref="AnsweredAt"/> / <see cref="AnswerBody"/> заполняет 1b). Read-only
///     проекция для UI-треда; порядок в списке — по <c>createdAt</c> возрастанию.
/// </summary>
public sealed record StudentPrMessageDto(
    Guid Id,
    long GithubCommentId,
    long? InReplyToGithubId,
    string AuthorGithubLogin,
    string Body,
    string? Path,
    int? Line,
    string CommentUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnsweredAt,
    string? AnswerBody);
