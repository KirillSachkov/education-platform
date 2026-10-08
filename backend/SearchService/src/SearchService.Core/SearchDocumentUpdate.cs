namespace SearchService.Core;

public sealed record SearchDocumentUpdate<TDocument>(
    string DocumentId,
    Action<PartialUpdate<TDocument>> Update)
    where TDocument : class;
