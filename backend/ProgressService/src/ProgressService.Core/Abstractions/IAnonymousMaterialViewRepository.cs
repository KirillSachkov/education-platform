namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий cookie-scoped просмотров материалов (<see cref="AnonymousMaterialView"/>).
///     Ключ: (AnonymousId, MaterialId). Запись создаётся один раз и не изменяется —
///     повторный POST с тем же anonymousId+materialId возвращает success без вставки.
/// </summary>
public interface IAnonymousMaterialViewRepository
{
    /// <summary>
    ///     Идемпотентная вставка через <c>INSERT ... ON CONFLICT DO NOTHING</c>.
    ///     Возвращает <c>true</c> если строка была вставлена, <c>false</c> если запись
    ///     с тем же ключом <c>(anonymous_id, material_id)</c> уже была. Один SQL вместо
    ///     ExistsAsync+Add+SaveChanges. Используется только write-handler'ом
    ///     <see cref="ProgressService.Core.Features.Materials.UseCases.RecordAnonymousMaterialViewHandler"/>.
    /// </summary>
    Task<bool> UpsertAsync(
        string anonymousId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Считает уникальные просмотры (auth + anon) для batch'а материалов одним SQL-запросом.
    ///     Используется батч-эндпоинтом <c>GET /progress/materials/views/counts</c> для
    ///     обогащения карточек материала на фронте. Возвращает map только по тем, у кого &gt; 0
    ///     просмотров; отсутствующие в карте материалы трактуются caller'ом как ноль.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, long>> GetTotalViewsCountsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Каскадное удаление всех анонимных просмотров материала. Используется обработчиком
    ///     <c>MaterialHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default);
}
