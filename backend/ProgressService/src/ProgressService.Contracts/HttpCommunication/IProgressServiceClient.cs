namespace ProgressService.Contracts.HttpCommunication;

/// <summary>
///     HTTP-клиент для межсервисного взаимодействия с ProgressService. На текущий момент
///     ECS пользуется им только для enrichment'а карточек/детали материала счётчиком
///     просмотров (issue #234). Расширяется по мере появления других use case'ов.
/// </summary>
public interface IProgressServiceClient
{
    /// <summary>
    ///     Возвращает количество уникальных просмотров для batch'а материалов. Не
    ///     возвращает строки с нулевыми счётчиками — caller трактует отсутствующий
    ///     id как 0.
    /// </summary>
    Task<Result<IReadOnlyDictionary<Guid, long>, Error>> GetMaterialViewsCountsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken);
}
