namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Issue #713: студент написал комментарий (reply на inline-коммент AI-ревьюера или
///     top-level коммент) в своём GitHub-PR — обратный канал к AI-проверке. ARS услышал это
///     через webhook (<c>pull_request_review_comment</c> / <c>issue_comment</c>), сохранил
///     <c>StudentPrMessage</c> и публикует это событие ровно один раз на новое сообщение
///     (идемпотентно — повторная доставка webhook'а не публикует повторно).
///
///     Событие самодостаточно — несёт всё, что нужно потребителям, без обратных вызовов:
///     <list type="bullet">
///         <item><b>NotificationService</b> — уведомляет автора курса (<see cref="AuthorId"/>)
///         «студент задал вопрос в PR»: текст (<see cref="Body"/>), кто (<see cref="StudentName"/> /
///         <see cref="StudentGithubLogin"/>), где (<see cref="PullRequestUrl"/> / <see cref="CommentUrl"/>),
///         по какому заданию (<see cref="IssueId"/> / <see cref="CourseId"/>).</item>
///         <item><b>ProgressService</b> — может связать вопрос с submission (<see cref="SubmissionId"/>).</item>
///     </list>
/// </summary>
public sealed record StudentPrQuestionAsked(
    Guid StudentPrMessageId,
    Guid AiReviewId,
    Guid SubmissionId,
    Guid IssueId,
    Guid? CourseId,
    Guid AuthorId,
    Guid? StudentUserId,
    string StudentGithubLogin,
    string? StudentName,
    string Body,
    string RepoFullName,
    int PullNumber,
    string PullRequestUrl,
    string CommentUrl,
    string? Path,
    int? Line,
    long GithubCommentId,
    DateTimeOffset CreatedAt);
