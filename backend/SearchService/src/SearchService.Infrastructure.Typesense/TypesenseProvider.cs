using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SearchService.Contracts;
using SearchService.Core;
using SharedKernel;
using Typesense;

namespace SearchService.Infrastructure.Typesense;

public sealed class TypesenseProvider : ISearchProvider
{
    private readonly ITypesenseClient _typesenseClient;
    private readonly ILogger<TypesenseProvider> _logger;

    // Scoped-safe: this field is set on RebuildAsync entry, read by
    // ResolveImportIndexName during the same call's import lambda, and cleared
    // in finally. Safe because TypesenseProvider is registered Scoped — each
    // RebuildAsync call has its own instance, no cross-thread sharing.
    // ⚠️ DO NOT switch this registration to Singleton without first making this
    // field thread-safe (Interlocked / volatile + per-call context) — concurrent
    // rebuilds would race on it.
    private RebuildImportContext? _rebuildImportContext;

    public TypesenseProvider(
        ITypesenseClient typesenseClient,
        ILogger<TypesenseProvider> logger)
    {
        _typesenseClient = typesenseClient;
        _logger = logger;
    }

    public async Task<Result<T, Error>> RebuildAsync<T>(
        string indexName,
        Func<CancellationToken, Task<Result<T, Error>>> rebuild,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        string rebuildCollectionName =
            $"{indexName}_rebuild_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.CreateVersion7():N}";
        bool aliasSwapped = false;

        try
        {
            string? previousCollectionName = await GetAliasTargetOrNullAsync(indexName, cancellationToken);

            await CreateEducationCollectionAsync(rebuildCollectionName, cancellationToken);

            _rebuildImportContext = new RebuildImportContext(indexName, rebuildCollectionName);

            Result<T, Error> rebuildResult = await rebuild(cancellationToken);
            if (rebuildResult.IsFailure)
            {
                await SafeDeleteCollectionAsync(rebuildCollectionName);
                return rebuildResult.Error;
            }

            await UpsertAliasAsync(indexName, rebuildCollectionName, cancellationToken);
            aliasSwapped = true;

            if (!string.IsNullOrWhiteSpace(previousCollectionName) &&
                !string.Equals(previousCollectionName, rebuildCollectionName, StringComparison.Ordinal))
            {
                await SafeDeleteCollectionAsync(previousCollectionName);
            }

            return rebuildResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!aliasSwapped)
            {
                await SafeDeleteCollectionAsync(rebuildCollectionName);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild search index {IndexName}", indexName);

            if (!aliasSwapped)
            {
                await SafeDeleteCollectionAsync(rebuildCollectionName);
            }

            return TypesenseErrorMapper.ToError(ex);
        }
        finally
        {
            _rebuildImportContext = null;
        }
    }

    public async Task<UnitResult<Error>> UpsertAsync<TDocument>(
        string indexName,
        TDocument document,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.UpsertDocument(indexName, document);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert document into index {IndexName}", indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> CreateAsync<TDocument>(
        string indexName,
        TDocument document,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Имя поискового индекса не может быть пустым");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.CreateDocument(indexName, document);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TypesenseApiConflictException ex)
        {
            _logger.LogDebug(
                ex,
                "Document already exists in index {IndexName}; caller will reconcile owned fields",
                indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document in index {IndexName}", indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> UpdateAsync<TDocument>(
        string indexName,
        string documentId,
        Action<PartialUpdate<TDocument>> update,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        var partialUpdate = new PartialUpdate<TDocument>();
        update(partialUpdate);
        Dictionary<string, object?> document = partialUpdate.Build();

        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Error.Validation("search.document_id.invalid", "Document id cannot be empty");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.UpdateDocument(indexName, documentId, document);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update document {DocumentId} in index {IndexName}",
                documentId,
                indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> EmplaceAsync<TDocument>(
        string indexName,
        string documentId,
        Action<PartialUpdate<TDocument>> update,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Error.Validation("search.document_id.invalid", "Document id cannot be empty");
        }

        var partialUpdate = new PartialUpdate<TDocument>();
        update(partialUpdate);
        Dictionary<string, object?> document = partialUpdate.Build();
        document["id"] = documentId;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ImportResponse> importResponses = await _typesenseClient.ImportDocuments(
                ResolveImportIndexName(indexName),
                [document],
                batchSize: 1,
                ImportType.Emplace,
                null,
                true);

            return EnsureImportSucceeded(
                importResponses,
                expectedDocumentsCount: 1,
                indexName,
                ImportType.Emplace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to emplace document {DocumentId} in index {IndexName}",
                documentId,
                indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> UpdateManyAsync<TDocument>(
        string indexName,
        IReadOnlyCollection<SearchDocumentUpdate<TDocument>> updates,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (updates.Count == 0)
        {
            return UnitResult.Success<Error>();
        }

        if (batchSize <= 0)
        {
            return Error.Validation("search.batch_size.invalid", "Batch size must be greater than zero");
        }

        List<Dictionary<string, object?>> documents = [];

        foreach (SearchDocumentUpdate<TDocument> update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.DocumentId))
            {
                return Error.Validation("search.document_id.invalid", "Document id cannot be empty");
            }

            var partialUpdate = new PartialUpdate<TDocument>();
            update.Update(partialUpdate);

            Dictionary<string, object?> document = partialUpdate.Build();
            document["id"] = update.DocumentId;
            documents.Add(document);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ImportResponse> importResponses = await _typesenseClient.ImportDocuments(
                ResolveImportIndexName(indexName),
                documents,
                batchSize,
                ImportType.Update,
                null,
                true);

            return EnsureImportSucceeded(
                importResponses,
                documents.Count,
                indexName,
                ImportType.Update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update documents in index {IndexName}", indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<SearchResponse<TDocument>, Error>> SearchAsync<TDocument>(
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
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(queryBy))
        {
            return Error.Validation("search.query.fields.empty", "At least one search field is required");
        }

        if (request.Page <= 0)
        {
            return Error.Validation("search.query.page.invalid", "Page must be greater than zero");
        }

        if (request.PageSize <= 0)
        {
            return Error.Validation("search.query.page_size.invalid", "PageSize must be greater than zero");
        }

        try
        {
            var searchParameters = new SearchParameters(
                string.IsNullOrWhiteSpace(request.Search) ? "*" : request.Search,
                queryBy)
            {
                FilterBy = filterBy,
                FacetBy = facetBy,
                IncludeFields = includeFields,
                ExcludeFields = excludeFields,
                HighlightFields = highlightFields,
                HighlightFullFields = highlightFullFields,
                SnippetThreshold = snippetThreshold,
                QueryByWeights = queryByWeights,
                MaxFacetValues = maxFacetValues,
                SortBy = sortBy,
                Page = request.Page,
                PerPage = request.PageSize,
                LimitHits = limitHits,
            };

            SearchResult<TDocument> result =
                await _typesenseClient.Search<TDocument>(indexName, searchParameters, cancellationToken);

            IReadOnlyList<SearchHit<TDocument>> hits = result.Hits
                .Select(static hit =>
                {
                    IReadOnlyList<SearchHighlight> highlights = hit.Highlights
                        .Select(static x =>
                        {
                            // Для string[]-полей (chapter_titles, tag_titles) Typesense
                            // отдаёт matched-результат через Snippets[]+Indices[],
                            // а Snippet/Value скаляры остаются null. Берём первый
                            // совпавший снипет как preview; индексы пробрасываем в
                            // SearchHighlight.MatchedIndices — фронт по ним резолвит
                            // конкретный chapter_timestamps[i] для deep-link.
                            string? arraySnippet = x.Snippets is { Count: > 0 } ? x.Snippets[0] : null;
                            string snippet = x.Snippet ?? arraySnippet ?? x.Value ?? string.Empty;
                            IReadOnlyList<int>? matchedIndices =
                                x.Indices is { Count: > 0 } ? x.Indices.ToArray() : null;
                            return new SearchHighlight(x.Field, snippet, matchedIndices);
                        })
                        .ToList();

                    return new SearchHit<TDocument>(hit.Document, hit.TextMatch, highlights);
                })
                .ToList();

            IReadOnlyList<SearchFacet> facets = result.FacetCounts
                .Select(static facet =>
                {
                    IReadOnlyList<SearchFacetValue> values = facet.Counts
                        .Select(static x => new SearchFacetValue(x.Value, x.Count))
                        .ToList();

                    return new SearchFacet(facet.FieldName, values);
                })
                .ToList();

            return new SearchResponse<TDocument>(
                hits,
                facets,
                result.FoundDocs ?? result.Found,
                request.Page,
                request.PageSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute search in index {IndexName}", indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<IReadOnlyList<TDocument>, Error>> ExportByFilterAsync<TDocument>(
        string indexName,
        string filterBy,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(filterBy))
        {
            return Error.Validation("search.query.filter.empty", "Filter cannot be empty");
        }

        try
        {
            List<TDocument> documents = await _typesenseClient.ExportDocuments<TDocument>(
                indexName,
                new ExportParameters { FilterBy = filterBy },
                cancellationToken);

            return documents;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to export documents from index {IndexName} using filter {FilterBy}",
                indexName,
                filterBy);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<Result<TDocument?, Error>> ExportByIdAsync<TDocument>(
        string indexName,
        string documentId,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Error.Validation("search.document_id.invalid", "Document id cannot be empty");
        }

        Result<IReadOnlyList<TDocument>, Error> documentsResult = await ExportByFilterAsync<TDocument>(
            indexName,
            $"id:=`{documentId}`",
            cancellationToken);

        if (documentsResult.IsFailure)
        {
            return documentsResult.Error;
        }

        return documentsResult.Value.SingleOrDefault();
    }

    public async Task<UnitResult<Error>> DeleteByFilterAsync(
        string indexName,
        string filterBy,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(filterBy))
        {
            return Error.Validation("search.query.filter.empty", "Filter cannot be empty");
        }

        if (batchSize <= 0)
        {
            return Error.Validation("search.batch_size.invalid", "Batch size must be greater than zero");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.DeleteDocuments(indexName, filterBy, batchSize);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete documents from index {IndexName} using filter {FilterBy}",
                indexName,
                filterBy);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> DeleteByIdAsync(
        string indexName,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Error.Validation("search.document_id.invalid", "Document id cannot be empty");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _typesenseClient.DeleteDocument<object>(indexName, documentId);
            return UnitResult.Success<Error>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {DocumentId} from index {IndexName}", documentId, indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    public async Task<UnitResult<Error>> ImportAsync<TDocument>(
        string indexName,
        IReadOnlyCollection<TDocument> documents,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Error.Validation("search.index.invalid", "Index name cannot be empty");
        }

        if (documents.Count == 0)
        {
            return UnitResult.Success<Error>();
        }

        if (batchSize <= 0)
        {
            return Error.Validation("search.batch_size.invalid", "Batch size must be greater than zero");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<ImportResponse> importResponses = await _typesenseClient.ImportDocuments(
                ResolveImportIndexName(indexName),
                documents,
                batchSize,
                ImportType.Upsert,
                null,
                true);

            return EnsureImportSucceeded(
                importResponses,
                documents.Count,
                indexName,
                ImportType.Upsert);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import documents into index {IndexName}", indexName);
            return TypesenseErrorMapper.ToError(ex);
        }
    }

    private UnitResult<Error> EnsureImportSucceeded(
        IReadOnlyList<ImportResponse> importResponses,
        int expectedDocumentsCount,
        string indexName,
        ImportType importType)
    {
        ImportResponse[] failedResponses = importResponses
            .Where(static response => !response.Success)
            .ToArray();

        if (failedResponses.Length == 0 && importResponses.Count == expectedDocumentsCount)
        {
            return UnitResult.Success<Error>();
        }

        string failures = string.Join(
            "; ",
            failedResponses
                .Take(5)
                .Select(static response =>
                    $"id={response.Id ?? "<unknown>"}, error={response.Error ?? "<empty>"}"));

        _logger.LogError(
            "Typesense import failed in index {IndexName}. ImportType: {ImportType}. FailedDocuments: {FailedDocuments}/{ExpectedDocuments}. ReturnedResponses: {ReturnedResponses}. Failures: {Failures}",
            indexName,
            importType,
            failedResponses.Length,
            expectedDocumentsCount,
            importResponses.Count,
            failures);

        return Error.Failure(
            "search.typesense.import_failed",
            "Typesense failed to import one or more search documents");
    }

    private async Task<string?> GetAliasTargetOrNullAsync(
        string aliasName,
        CancellationToken cancellationToken)
    {
        try
        {
            CollectionAliasResponse alias =
                await _typesenseClient.RetrieveCollectionAlias(aliasName, cancellationToken);

            return alias.CollectionName;
        }
        catch (TypesenseApiNotFoundException)
        {
            return null;
        }
    }

    private async Task CreateEducationCollectionAsync(
        string collectionName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _typesenseClient.CreateCollection(TypesenseSchemas.CreateEducationSearchSchema(collectionName));
    }

    private async Task UpsertAliasAsync(
        string aliasName,
        string collectionName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var alias = new CollectionAlias(collectionName);
        await _typesenseClient.UpsertCollectionAlias(aliasName, alias);
    }

    private async Task SafeDeleteCollectionAsync(string collectionName)
    {
        try
        {
            await _typesenseClient.DeleteCollection(collectionName, compactStore: true);
        }
        catch (TypesenseApiNotFoundException)
        {
            // Collection already gone — idempotent cleanup, nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Typesense collection {CollectionName}", collectionName);
        }
    }

    private string ResolveImportIndexName(string indexName)
    {
        RebuildImportContext? context = _rebuildImportContext;

        return context is not null &&
            string.Equals(context.IndexName, indexName, StringComparison.Ordinal)
                ? context.CollectionName
                : indexName;
    }

    private sealed record RebuildImportContext(string IndexName, string CollectionName);
}
