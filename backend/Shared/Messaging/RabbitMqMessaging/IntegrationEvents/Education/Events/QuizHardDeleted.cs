namespace Shared.Messaging.IntegrationEvents.Education.Events;

/// <summary>
/// Published when a quiz is hard-deleted (#490) — зеркало <see cref="MaterialHardDeleted"/>.
/// ECS self-consume handler удаляет Redis-ключ тегов доступа квиза.
/// </summary>
public sealed record QuizHardDeleted(Guid QuizId);
