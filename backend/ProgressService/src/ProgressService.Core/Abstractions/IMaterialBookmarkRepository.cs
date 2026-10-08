using System.Linq.Expressions;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Core.Abstractions;

public interface IMaterialBookmarkRepository
{
    Task<bool> AddIfMissingAsync(MaterialBookmark bookmark, CancellationToken cancellationToken = default);

    void Remove(MaterialBookmark bookmark);

    Task<MaterialBookmark?> GetByAsync(
        Expression<Func<MaterialBookmark, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<MaterialBookmark, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет все закладки, принадлежащие указанному курсу.
    ///     Используется обработчиком <c>CourseHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет закладки, указывающие на любой из перечисленных target-entity id (material/issue/module).
    ///     Используется обработчиками <c>MaterialHardDeleted</c>, <c>IssueHardDeleted</c>, <c>ModuleHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByTargetEntityIdsAsync(
        IReadOnlyCollection<Guid> targetEntityIds,
        CancellationToken cancellationToken = default);
}
