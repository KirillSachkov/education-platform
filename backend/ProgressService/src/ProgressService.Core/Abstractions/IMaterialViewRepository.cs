using System.Linq.Expressions;
using ProgressService.Domain.Materials;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий user-scoped просмотров материалов (<see cref="MaterialView"/>).
///     Ключ: (UserId, MaterialId). Одна строка имеет два состояния (issue #285):
///     <c>IsCompleted=false</c> — silent track для счётчика «N просмотров», без cascade;
///     <c>IsCompleted=true</c> — явная отметка «Изучено», cascade на module_item_progress.
/// </summary>
public interface IMaterialViewRepository
{
    Task AddAsync(MaterialView materialView, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает запись о просмотре, если она есть. Состояние (<c>IsCompleted</c>)
    ///     не фильтруется — caller сам решает, что делать с track-view'ом.
    /// </summary>
    Task<Result<MaterialView, Error>> GetByAsync(
        Expression<Func<MaterialView, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает map ИЗУЧЕННЫХ (<c>is_completed=true</c>) материалов из списка с timestamp'ами
    ///     для текущего пользователя. Silent track-view'ы (<c>is_completed=false</c>) сюда не попадают —
    ///     они не означают «изучено». Используется в <c>GetMaterialViewStatus</c> для batch-ответа фронту:
    ///     фронт рендерит «Изучено DD.MM.YYYY» на карточках материалов. Issue #285.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetCompletedMaterialMapAsync(
        Guid userId,
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Идемпотентно создаёт silent view track (<c>is_completed=false</c>). Если запись для
    ///     пары (UserId, MaterialId) уже есть в любом состоянии — no-op, существующая
    ///     <c>is_completed</c> не понижается. Используется по mount detail-страницы материала
    ///     для счётчика «N просмотров» — без cascade. Issue #285.
    /// </summary>
    /// <returns><c>true</c> если новая строка была вставлена, иначе <c>false</c>.</returns>
    Task<bool> TryInsertTrackAsync(
        Guid userId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Атомарно создаёт completed-view или апгрейдит существующий silent track.
    ///     Нужен для race <c>track-view</c> на mount vs быстрый explicit mark: оба запроса
    ///     бьются в один unique-key <c>(user_id, material_id)</c>.
    /// </summary>
    Task<Result<MaterialViewCompletionResult, Error>> CompleteAsync(
        Guid userId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет все записи о просмотре для данного материала. Используется
    ///     обработчиком <c>MaterialHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Помечает запись на удаление в текущей транзакции. Фактическое удаление произойдёт
    ///     при <see cref="ITransactionManager.SaveChangesAsync"/>. Применяется в edge-case'ах
    ///     (например, тесты); product-flow «снятия отметки» использует <c>MarkAsCompleted/UnmarkAsCompleted</c>
    ///     и оставляет строку как silent track.
    /// </summary>
    void Remove(MaterialView materialView);
}

public sealed record MaterialViewCompletionResult(DateTime ViewedAt, bool StateChanged);