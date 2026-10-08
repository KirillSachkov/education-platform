namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Тип комментария студента в PR, который платформа услышала через webhook (#713).
///     <list type="bullet">
///         <item><see cref="REVIEW_COMMENT"/> — inline / threaded reply на строке diff'а
///         (<c>pull_request_review_comment</c>): несёт <c>path</c> + <c>line</c> и может быть
///         reply'ем на конкретный inline-коммент бота (<c>in_reply_to_id</c>).</item>
///         <item><see cref="ISSUE_COMMENT"/> — top-level коммент в ленте PR
///         (<c>issue_comment</c> с <c>issue.pull_request</c>): без привязки к строке.</item>
///     </list>
///     Хранится строкой через <c>HasConversion&lt;string&gt;()</c> (UPPER_SNAKE_CASE —
///     единый casing enum'ов платформы, см. backend/CLAUDE.md § Enum Storage Convention).
/// </summary>
public enum StudentPrMessageKind
{
    REVIEW_COMMENT,
    ISSUE_COMMENT,
}
