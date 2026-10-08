namespace SearchService.Core.Reindex;

/// <summary>
/// Отдаёт стабильный hash текущей поисковой схемы (поля Typesense-коллекции).
/// Любое изменение схемы в коде меняет hash → startup-check видит mismatch с
/// <c>search.reindex_state.applied_schema_hash</c> и триггерит полный реиндекс
/// без ручного бампа generation (#526).
/// </summary>
public interface ISearchSchemaVersionProvider
{
    string SchemaHash { get; }
}
