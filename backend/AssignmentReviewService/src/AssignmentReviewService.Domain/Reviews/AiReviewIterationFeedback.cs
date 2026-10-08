namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Фидбэк автора на AI-iteration: «полезно / не полезно» + опциональный
///     комментарий. Issue #327 — нужно для оценки качества AI-ревью без вытягивания
///     метрик из логов.
///
///     Один автор может оставить ровно один фидбэк на iteration; повторный
///     submit upsert'ит существующий через unique(iteration_id, user_id).
/// </summary>
public sealed class AiReviewIterationFeedback
{
    private AiReviewIterationFeedback() { } // EF

    private AiReviewIterationFeedback(
        Guid iterationId,
        Guid userId,
        bool isHelpful,
        string? comment,
        DateTimeOffset createdAt)
    {
        Id = Guid.Empty; // EF ValueGenerator
        IterationId = iterationId;
        UserId = userId;
        IsHelpful = isHelpful;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid IterationId { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsHelpful { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public const int COMMENT_MAX_LENGTH = 1_000;

    public static AiReviewIterationFeedback Create(
        Guid iterationId,
        Guid userId,
        bool isHelpful,
        string? comment)
    {
        return new AiReviewIterationFeedback(
            iterationId,
            userId,
            isHelpful,
            comment,
            DateTimeOffset.UtcNow);
    }

    public void Update(bool isHelpful, string? comment)
    {
        IsHelpful = isHelpful;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
