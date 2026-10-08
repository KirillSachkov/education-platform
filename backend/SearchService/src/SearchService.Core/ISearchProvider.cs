using SearchService.Contracts;

namespace SearchService.Core;

/// <summary>
/// Контракт поставщика поиска для индексации, поиска и удаления документов.
/// </summary>
public interface ISearchProvider
{
    Task<Result<T, Error>> RebuildAsync<T>(
        string indexName,
        Func<CancellationToken, Task<Result<T, Error>>> rebuild,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет поиск в указанном индексе по заданному запросу.
    /// </summary>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="request">Параметры поиска.</param>
    /// <param name="queryBy">Поля индекса, по которым выполняется поиск.</param>
    /// <param name="filterBy">Серверный фильтр поиска.</param>
    /// <param name="facetBy">Серверные поля фасетов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат поиска с ответом или ошибкой.</returns>
    Task<Result<SearchResponse<TDocument>, Error>> SearchAsync<TDocument>(
        string indexName,
        SearchRequest request,
        string queryBy,
        string? filterBy = null,
        string? facetBy = null,
        string? includeFields = null,
        string? queryByWeights = null,
        int? maxFacetValues = null,
        string? excludeFields = null,
        string? highlightFields = null,
        string? highlightFullFields = null,
        int? snippetThreshold = null,
        string? sortBy = null,
        int? limitHits = null,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Создает или обновляет документ в указанном индексе.
    /// </summary>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="document">Документ для индексации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции без полезной нагрузки.</returns>
    Task<UnitResult<Error>> UpsertAsync<TDocument>(
        string indexName,
        TDocument document,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Creates a document and returns a conflict when the id already exists.
    /// </summary>
    Task<UnitResult<Error>> CreateAsync<TDocument>(
        string indexName,
        TDocument document,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Создает или обновляет несколько документов в указанном индексе.
    /// </summary>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="documents">Список документов для индексации.</param>
    /// <param name="batchSize">Размер пакета для индексации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции без полезной нагрузки.</returns>
    Task<UnitResult<Error>> ImportAsync<TDocument>(
        string indexName,
        IReadOnlyCollection<TDocument> documents,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Обновляет документ в указанном индексе.
    /// </summary>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="documentId">Идентификатор документа.</param>
    /// <param name="update">Действия для обновления документа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции без полезной нагрузки.</returns>
    Task<UnitResult<Error>> UpdateAsync<TDocument>(
        string indexName,
        string documentId,
        Action<PartialUpdate<TDocument>> update,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Creates a missing document or updates only the supplied fields of an existing one.
    /// Unspecified fields are preserved atomically by Typesense's emplace action.
    /// </summary>
    Task<UnitResult<Error>> EmplaceAsync<TDocument>(
        string indexName,
        string documentId,
        Action<PartialUpdate<TDocument>> update,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    Task<UnitResult<Error>> UpdateManyAsync<TDocument>(
        string indexName,
        IReadOnlyCollection<SearchDocumentUpdate<TDocument>> updates,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Экспортирует документы из указанного индекса по заданному фильтру.
    /// </summary>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="filterBy">Фильтр для выборки документов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <returns>Результат экспорта документов или ошибки.</returns>
    Task<Result<IReadOnlyList<TDocument>, Error>> ExportByFilterAsync<TDocument>(
        string indexName,
        string filterBy,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Экспортирует документ из указанного индекса по его идентификатору.
    /// </summary>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="documentId">Идентификатор документа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <typeparam name="TDocument">Тип документа.</typeparam>
    /// <returns>Результат экспорта документа или ошибки.</returns>
    Task<Result<TDocument?, Error>> ExportByIdAsync<TDocument>(
        string indexName,
        string documentId,
        CancellationToken cancellationToken = default)
        where TDocument : class;

    /// <summary>
    /// Удаляет документы из указанного индекса по заданному фильтру.
    /// </summary>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="filterBy">Фильтр документов для удаления.</param>
    /// <param name="batchSize">Размер пакета удаления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции без полезной нагрузки.</returns>
    Task<UnitResult<Error>> DeleteByFilterAsync(
        string indexName,
        string filterBy,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет документ из указанного индекса.
    /// </summary>
    /// <param name="indexName">Имя индекса.</param>
    /// <param name="documentId">Идентификатор документа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции без полезной нагрузки.</returns>
    Task<UnitResult<Error>> DeleteByIdAsync(
        string indexName,
        string documentId,
        CancellationToken cancellationToken = default);
}
