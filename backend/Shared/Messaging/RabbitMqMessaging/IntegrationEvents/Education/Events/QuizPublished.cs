namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
/// Published when a quiz transitions to the Published state (#490).
/// С этого момента квиз становится доступен студентам — ECS self-consume
/// handler выставляет Redis-теги доступа (resource type <c>quiz</c>).
/// </summary>
/// <param name="AccessType">Собственный уровень доступа квиза: PUBLIC | REGISTERED | ENROLLED.</param>
/// <param name="CourseIds">Курсы, к которым привязан квиз (<c>course_quizzes</c>); может быть пуст (standalone).</param>
public sealed record QuizPublished(
    Guid QuizId,
    Guid AuthorId,
    string AccessType,
    IReadOnlyList<Guid> CourseIds);
