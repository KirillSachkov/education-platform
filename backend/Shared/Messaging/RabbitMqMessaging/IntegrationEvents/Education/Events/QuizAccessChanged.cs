namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
/// Published when a quiz's <c>AccessType</c> changes (#490) — зеркало
/// <see cref="MaterialAccessChanged"/>. ECS self-consume handler пересчитывает
/// Redis-теги (только для PUBLISHED-квизов — у DRAFT тегов нет до публикации).
/// </summary>
/// <param name="AccessType">Новый уровень доступа: PUBLIC | REGISTERED | ENROLLED.</param>
/// <param name="CourseIds">Курсы, к которым привязан квиз (<c>course_quizzes</c>) на момент события.</param>
public sealed record QuizAccessChanged(
    Guid QuizId,
    string AccessType,
    IReadOnlyList<Guid> CourseIds,
    Guid AuthorId);
