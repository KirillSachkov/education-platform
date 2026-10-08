using System.Linq.Expressions;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using Ordering;

namespace EducationContentService.Core.Features.Courses;

public interface ICoursesRepository : IOrderedItemsRepository<Course>
{
    /// <summary>
    ///     Convenience overload (without orderBy) — most call-sites just need the predicate.
    ///     The <see cref="IOrderedItemsRepository{T}.GetByAsync"/> overload with orderBy is still
    ///     available via the base interface for ordering use-cases.
    /// </summary>
    Task<Result<Course, Error>> GetByAsync(
        Expression<Func<Course, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<Course, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Course course, CancellationToken ct = default);

    Task<long> GetNextAssetOwnershipRevisionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Уникальность заголовка среди non-DRAFT курсов (PUBLISHED + ARCHIVED).
    ///     Бизнес-правило вынесено в репозиторий, чтобы не дублировать фильтр статусов в use-cases.
    /// </summary>
    Task<bool> ExistsByTitleAsync(Title title, Guid excludeId, CancellationToken ct = default);

    /// <summary>
    ///     Bulk-reassigns the author of all content owned exclusively by the course —
    ///     its modules, projects, those projects' issues, course-level collections, and the
    ///     materials/quizzes attached ONLY to this course. Materials/quizzes shared with
    ///     other courses are left under the previous author and returned in the result (#587).
    ///     Runs as raw SQL on the caller's ambient transaction (the caller flips the course
    ///     root and commits).
    /// </summary>
    Task<CourseAuthorReassignmentResult> ReassignChildContentAuthorAsync(
        Guid courseId,
        Guid newAuthorId,
        CancellationToken ct = default);
}

/// <summary>
///     Outcome of <see cref="ICoursesRepository.ReassignChildContentAuthorAsync"/> — the
///     ids of materials/quizzes that were skipped because they are shared with other courses.
/// </summary>
public sealed record CourseAuthorReassignmentResult(
    IReadOnlyList<Guid> SkippedSharedMaterialIds,
    IReadOnlyList<Guid> SkippedSharedQuizIds,
    IReadOnlyList<Guid> TransferredProjectIds,
    IReadOnlyList<Guid> TransferredIssueIds,
    IReadOnlyList<Guid> TransferredMaterialIds,
    IReadOnlyList<Guid> TransferredCollectionIds);
