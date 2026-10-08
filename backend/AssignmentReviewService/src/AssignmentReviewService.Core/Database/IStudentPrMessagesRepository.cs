using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Database;

/// <summary>
///     Доступ к <see cref="StudentPrMessage"/> — комментарии студента в его PR (#713).
///     Write через <c>ITransactionManager.SaveChangesAsync</c> (репо не сохраняет само),
///     кроме targeted <see cref="MarkAnsweredAsync"/> (raw-SQL UPDATE, auto-commit —
///     как <c>HeartbeatRunningAsync</c> в <c>AiReviewsRepository</c>).
/// </summary>
public interface IStudentPrMessagesRepository
{
    Task AddAsync(StudentPrMessage message, CancellationToken ct = default);

    /// <summary>
    ///     Одно сообщение по его id или null. Read-модель для reply-эндпоинта автора (1b):
    ///     резолвит <see cref="StudentPrMessage.AiReviewId"/> для проверки прав и адреса треда.
    /// </summary>
    Task<StudentPrMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    ///     Идемпотентность ingest'а: существует ли уже сообщение с данным GitHub comment id.
    ///     Повторная доставка того же webhook'а (GitHub ретраит) → true → skip.
    /// </summary>
    Task<bool> ExistsByGitHubCommentIdAsync(long gitHubCommentId, CancellationToken ct = default);

    /// <summary>
    ///     Все сообщения студента по конкретной проверке, по возрастанию
    ///     <see cref="StudentPrMessage.CreatedAtGithub"/>. Read-модель для by-submission DTO (1a)
    ///     и списка тредов автору (1b/1c) — тред показывает ВСЕ реплики, поэтому без лимита.
    /// </summary>
    Task<IReadOnlyList<StudentPrMessage>> GetByAiReviewIdAsync(
        Guid aiReviewId, CancellationToken ct = default);

    /// <summary>
    ///     Последние <paramref name="limit"/> сообщений студента по проверке (#713) —
    ///     <c>ORDER BY created_at_github DESC LIMIT @limit</c>, но возвращаются в
    ///     хронологическом порядке (по возрастанию) для prompt-контекста. Свежие реплики
    ///     релевантнее для текущей итерации ревью; без лимита промпт грузил бы весь тред.
    ///     В отличие от <see cref="GetByAiReviewIdAsync"/> (полный тред для панели) — это
    ///     bounded read строго под AI-контекст.
    /// </summary>
    Task<IReadOnlyList<StudentPrMessage>> GetRecentByAiReviewIdAsync(
        Guid aiReviewId, int limit, CancellationToken ct = default);

    /// <summary>
    ///     Отметить сообщение отвеченным автором (1b). Targeted raw-SQL UPDATE (auto-commit).
    /// </summary>
    Task MarkAnsweredAsync(
        Guid messageId,
        string answerBody,
        long? answerGitHubCommentId,
        DateTimeOffset answeredAt,
        CancellationToken ct = default);
}
