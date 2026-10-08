using SharedKernel.DomainEvents;

namespace TrainerService.Domain.Bookmarks;

/// <summary>
/// Aggregate root: вопрос, закладку на который поставил пользователь. Unique на
/// (UserId, QuestionId).
/// </summary>
public sealed class BookmarkedQuestion : AggregateRoot
{
    private BookmarkedQuestion() { } // EF

    private BookmarkedQuestion(Guid id, Guid userId, Guid topicId, Guid questionId)
    {
        Id = id;
        UserId = userId;
        TopicId = topicId;
        QuestionId = questionId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? TopicId { get; private set; }

    public Guid QuestionId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static BookmarkedQuestion Create(Guid userId, Guid topicId, Guid questionId) =>
        new(Guid.CreateVersion7(), userId, topicId, questionId);
}