using System.Linq.Expressions;
using ProgressService.Domain.Modules;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий прогресса по элементам модуля.
/// </summary>
public interface IModuleItemProgressRepository
{
    Task AddAsync(ModuleItemProgress moduleItemProgress, CancellationToken cancellationToken = default);

    Task<Result<ModuleItemProgress, Error>> GetByAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleItemProgress>> GetManyByAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<int> CountCompletedByEnrollmentAndModuleAsync(
        Guid enrollmentId,
        Guid moduleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет все module_item_progress по reference_id (material, issue или quiz id).
    ///     Используется обработчиками <c>MaterialHardDeleted</c>, <c>IssueHardDeleted</c>,
    ///     <c>QuizHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет все module_item_progress по module_id.
    ///     Используется обработчиком <c>ModuleHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByModuleIdAsync(Guid moduleId, CancellationToken cancellationToken = default);
}
