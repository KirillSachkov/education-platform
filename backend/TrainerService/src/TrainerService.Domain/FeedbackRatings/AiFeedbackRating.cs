using SharedKernel.DomainEvents;

namespace TrainerService.Domain.FeedbackRatings;

/// <summary>
/// Aggregate root: оценка студентом AI-разбора («Разбор ИИ») одного отвеченного открытого
/// вопроса сессии — палец вверх/вниз (#691 t7). Unique на (UserId, SessionItemId) — одна оценка
/// на item у пользователя, переключаемая (UP↔DOWN). <see cref="QuestionId"/> денормализован, чтобы
/// admin-агрегат «на каких вопросах ИИ оценивает плохо» считался <c>GROUP BY question_id</c> без
/// дорогого join'а к снапшоту item'а.
/// </summary>
public sealed class AiFeedbackRating : AggregateRoot
{
    private AiFeedbackRating() { } // EF

    private AiFeedbackRating(
        Guid id,
        Guid userId,
        Guid sessionId,
        Guid sessionItemId,
        Guid questionId,
        FeedbackRating rating)
    {
        Id = id;
        UserId = userId;
        SessionId = sessionId;
        SessionItemId = sessionItemId;
        QuestionId = questionId;
        Rating = rating;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid SessionItemId { get; private set; }

    /// <summary>Id вопроса (снят с item'а) — денорм для per-question admin-агрегата.</summary>
    public Guid QuestionId { get; private set; }

    public FeedbackRating Rating { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AiFeedbackRating Create(
        Guid userId,
        Guid sessionId,
        Guid sessionItemId,
        Guid questionId,
        FeedbackRating rating) =>
        new(Guid.CreateVersion7(), userId, sessionId, sessionItemId, questionId, rating);

    /// <summary>Переключает оценку (UP↔DOWN) и обновляет <see cref="UpdatedAt"/>.</summary>
    public void ChangeRating(FeedbackRating rating)
    {
        Rating = rating;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
