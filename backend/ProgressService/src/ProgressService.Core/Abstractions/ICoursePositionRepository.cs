using System.Linq.Expressions;
using ProgressService.Domain.CoursePositions;

namespace ProgressService.Core.Abstractions;

/// <summary>
/// Репозиторий «последняя точка пользователя в курсе» (<see cref="CoursePosition"/>).
/// Ключ: (UserId, CourseId). Один ряд на пару — каждое открытие материала/задания
/// перезаписывает запись через atomic upsert.
/// </summary>
public interface ICoursePositionRepository
{
    /// <summary>
    /// Atomic upsert по уникальному ключу (UserId, CourseId): INSERT или UPDATE
    /// EntityType/EntityId/OpenedAt одной командой. Идемпотентен и race-safe —
    /// два конкурентных открытия не порождают дубль и не теряют последнее значение.
    /// </summary>
    Task UpsertAsync(
        Guid userId,
        Guid courseId,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    Task<CoursePosition?> GetByAsync(
        Expression<Func<CoursePosition, bool>> predicate,
        CancellationToken cancellationToken = default);
}
