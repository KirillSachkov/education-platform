using System.Linq.Expressions;
using ProgressService.Domain.Notes;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий личных заметок к материалам (<see cref="MaterialNote"/>).
///     Ключ: (UserId, MaterialId) — одна заметка на пару. Issue #465.
/// </summary>
public interface IMaterialNoteRepository
{
    Task AddAsync(MaterialNote note, CancellationToken cancellationToken = default);

    Task<MaterialNote?> GetByAsync(
        Expression<Func<MaterialNote, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Помечает заметку на удаление в текущей транзакции. Фактическое удаление —
    ///     при <see cref="Core.Database.ITransactionManager.SaveChangesAsync"/>.
    /// </summary>
    void Remove(MaterialNote note);

    Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default);
}
