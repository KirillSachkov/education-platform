using System.Linq.Expressions;
using ProgressService.Domain.Modules;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий прогресса по модулям.
/// </summary>
public interface IModuleProgressRepository
{
    Task AddAsync(ModuleProgress moduleProgress, CancellationToken cancellationToken = default);

    Task<Result<ModuleProgress, Error>> GetByAsync(
        Expression<Func<ModuleProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<ModuleProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет весь прогресс по модулю. Используется обработчиком <c>ModuleHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByModuleIdAsync(Guid moduleId, CancellationToken cancellationToken = default);
}
