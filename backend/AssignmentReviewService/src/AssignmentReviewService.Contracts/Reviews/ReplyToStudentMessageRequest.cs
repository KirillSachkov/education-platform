namespace AssignmentReviewService.Contracts.Reviews;

/// <summary>
///     Issue #713 (1b) — тело ответа автора курса на реплику студента в PR. Постится
///     обратно в тот же тред GitHub и сохраняется в <c>student_pr_messages.answer_*</c>.
/// </summary>
public sealed record ReplyToStudentMessageRequest(string Body);

/// <summary>
///     Результат ответа автора (#713 1b): id + html_url созданного в GitHub коммента-ответа
///     и момент, когда сообщение помечено отвеченным.
/// </summary>
public sealed record ReplyToStudentMessageResponse(
    Guid MessageId,
    long AnswerGithubCommentId,
    string AnswerHtmlUrl,
    DateTimeOffset AnsweredAt);
