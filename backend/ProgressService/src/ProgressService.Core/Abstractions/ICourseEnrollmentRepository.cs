using System.Linq.Expressions;
using ProgressService.Domain.Enrollments;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий записей на курс (lazy progress-anchor'ов).
/// </summary>
public interface ICourseEnrollmentRepository
{
    Task AddAsync(CourseEnrollment item, CancellationToken cancellationToken = default);

    Task<Result<CourseEnrollment, Error>> GetByAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<List<CourseEnrollment>> GetManyByAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет все записи на курс (каскадно удаляются module_progress, module_item_progress,
    ///     issue_progress, project_progress по FK). material_views — user-scoped, не привязаны
    ///     к enrollment и живут независимо. Используется обработчиком <c>CourseHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default);
}
