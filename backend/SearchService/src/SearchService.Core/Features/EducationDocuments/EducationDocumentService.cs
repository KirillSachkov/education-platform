using Common;
using Microsoft.Extensions.Options;
using SearchService.Contracts;
using SearchService.Core.Reindex;
using SearchService.Domain;
using TagService.Contracts.HttpCommunication;
using TagService.Contracts.SearchLookup;

namespace SearchService.Core.Features.EducationDocuments;

public sealed class EducationDocumentService
{
    private readonly ISearchProvider _searchProvider;
    private readonly ITagServiceClient _tagServiceClient;
    private readonly SearchReindexOptions _options;
    private readonly ILogger<EducationDocumentService> _logger;

    public EducationDocumentService(
        ISearchProvider searchProvider,
        ITagServiceClient tagServiceClient,
        IOptions<SearchReindexOptions> options,
        ILogger<EducationDocumentService> logger)
    {
        _searchProvider = searchProvider;
        _tagServiceClient = tagServiceClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<Result<T, Error>> RebuildAsync<T>(
        Func<CancellationToken, Task<Result<T, Error>>> rebuild,
        CancellationToken cancellationToken = default) =>
        _searchProvider.RebuildAsync(CollectionNames.EDUCATION_SEARCH, rebuild, cancellationToken);

    public Task<Result<SearchResponse<EducationDocument>, Error>> SearchAsync(
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
        CancellationToken cancellationToken = default) =>
        _searchProvider.SearchAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            request,
            queryBy,
            filterBy,
            facetBy,
            includeFields,
            queryByWeights,
            maxFacetValues,
            excludeFields,
            highlightFields,
            highlightFullFields,
            snippetThreshold,
            sortBy,
            limitHits,
            cancellationToken);

    public Task<UnitResult<Error>> ImportAsync(
        IReadOnlyCollection<EducationDocument> documents,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        _searchProvider.ImportAsync(
            CollectionNames.EDUCATION_SEARCH,
            documents,
            batchSize,
            cancellationToken);

    public Task<UnitResult<Error>> UpsertAsync(
        EducationDocument document,
        CancellationToken cancellationToken = default) =>
        _searchProvider.UpsertAsync(
            CollectionNames.EDUCATION_SEARCH,
            document,
            cancellationToken);

    // Atomic emplace closes the lifecycle/tag race: it creates a missing document, but when
    // a tag event has already written TagIds/TagTitles it updates every other field without
    // overwriting those independently-owned fields. It also removes the old read-before-write.
    public Task<UnitResult<Error>> UpsertPreservingTagsAsync(
        EducationDocument document,
        CancellationToken cancellationToken = default) =>
        _searchProvider.EmplaceAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            document.Id,
            update => update
                .Set(x => x.EntityId, document.EntityId)
                .Set(x => x.EntityType, document.EntityType)
                .Set(x => x.Title, document.Title)
                .Set(x => x.Description, document.Description)
                .Set(x => x.ImageId, document.ImageId)
                .Set(x => x.RequiredAccessTags, document.RequiredAccessTags)
                .Set(x => x.CourseId, document.CourseId)
                .Set(x => x.CourseSlug, document.CourseSlug)
                .Set(x => x.CourseTitle, document.CourseTitle)
                .Set(x => x.CourseAccessType, document.CourseAccessType)
                .Set(x => x.AuthorId, document.AuthorId)
                .Set(x => x.ProjectId, document.ProjectId)
                .Set(x => x.ProjectTitle, document.ProjectTitle)
                .Set(x => x.ModuleId, document.ModuleId)
                .Set(x => x.ModuleTitle, document.ModuleTitle)
                .Set(x => x.MaterialKind, document.MaterialKind)
                .Set(x => x.Content, document.Content)
                .Set(x => x.VideoId, document.VideoId)
                .Set(x => x.ChapterTitles, document.ChapterTitles)
                .Set(x => x.ChapterTimestamps, document.ChapterTimestamps)
                .Set(x => x.UpdatedAtTicks, document.UpdatedAtTicks)
                .Set(x => x.IsDeleted, document.IsDeleted)
                .Set(x => x.ReindexGeneration, document.ReindexGeneration),
            cancellationToken);

    public Task<Result<EducationDocument?, Error>> ExportByIdAsync(
        string documentId,
        CancellationToken cancellationToken = default) =>
        _searchProvider.ExportByIdAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            documentId,
            cancellationToken);

    public async Task<UnitResult<Error>> SetDeletedAsync(
        string documentId,
        bool isDeleted,
        CancellationToken cancellationToken = default)
    {
        Result<EducationDocument?, Error> existingDocumentResult =
            await ExportByIdAsync(documentId, cancellationToken);

        if (existingDocumentResult.IsFailure)
        {
            return existingDocumentResult.Error;
        }

        if (existingDocumentResult.Value is null)
        {
            return UnitResult.Success<Error>();
        }

        return await _searchProvider.UpdateAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            documentId,
            update => update
                .Set(x => x.IsDeleted, isDeleted)
                .Set(x => x.UpdatedAtTicks, DateTime.UtcNow.Ticks),
            cancellationToken);
    }

    public Task<UnitResult<Error>> UpdateTagsAsync(
        IReadOnlyCollection<EducationDocumentTagUpdate> updates,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        _searchProvider.UpdateManyAsync(
            CollectionNames.EDUCATION_SEARCH,
            updates
                .Select(update => new SearchDocumentUpdate<EducationDocument>(
                    update.DocumentId,
                    partial => partial
                        .Set(x => x.TagIds, update.TagIds)
                        .Set(x => x.TagTitles, update.TagTitles)
                        .Set(x => x.UpdatedAtTicks, DateTime.UtcNow.Ticks)))
                .ToArray(),
            batchSize,
            cancellationToken);

    public async Task<UnitResult<Error>> DeleteByIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        Result<EducationDocument?, Error> existingDocumentResult =
            await ExportByIdAsync(documentId, cancellationToken);

        if (existingDocumentResult.IsFailure)
        {
            return existingDocumentResult.Error;
        }

        if (existingDocumentResult.Value is null)
        {
            return UnitResult.Success<Error>();
        }

        return await _searchProvider.DeleteByIdAsync(
            CollectionNames.EDUCATION_SEARCH,
            documentId,
            cancellationToken);
    }

    public Task<UnitResult<Error>> DeleteByFilterAsync(
        string filterBy,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        _searchProvider.DeleteByFilterAsync(
            CollectionNames.EDUCATION_SEARCH,
            filterBy,
            batchSize,
            cancellationToken);

    public async Task<UnitResult<Error>> RefreshEntityTagsAsync(
        EntityType entityType,
        Guid entityId,
        bool createPendingDocument,
        CancellationToken cancellationToken = default)
    {
        string documentId = EducationDocument.CreateId(entityType, entityId);

        if (!Constants.SEARCHABLE_ENTITY_TYPES.Contains(entityType))
        {
            _logger.LogInformation(
                "Removing unsupported entity from search tag projection. EntityType: {EntityType}, EntityId: {EntityId}",
                entityType,
                entityId);
            return await DeleteByIdAsync(documentId, cancellationToken);
        }

        Result<EducationDocument?, Error> documentResult =
            await ExportByIdAsync(documentId, cancellationToken);

        if (documentResult.IsFailure)
        {
            return documentResult.Error;
        }

        Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error> lookupResult =
            await _tagServiceClient.GetEntitiesTagsSearchLookupAsync(
                [new EntityTagsSearchLookupBatchItem(entityType, entityId)],
                cancellationToken);

        if (lookupResult.IsFailure)
        {
            return lookupResult.Error;
        }

        EntityTagsSearchLookupBatchDto? tags = lookupResult.Value.SingleOrDefault();
        Guid[] resolvedTagIds = tags?.TagIds.Distinct().ToArray() ?? [];
        string[] resolvedTagTitles = tags?.TagTitles.Distinct(StringComparer.Ordinal).ToArray() ?? [];

        EducationDocument? document = documentResult.Value;

        if (document is null)
        {
            if (!createPendingDocument || resolvedTagIds.Length == 0)
            {
                _logger.LogInformation(
                    "Skipping tag sync because document was not found. EntityType: {EntityType}, EntityId: {EntityId}",
                    entityType,
                    entityId);
                return UnitResult.Success<Error>();
            }

            return await CreatePendingOrUpdateExistingTagsAsync(
                entityType,
                entityId,
                resolvedTagIds,
                resolvedTagTitles,
                cancellationToken);
        }

        return await UpdateTagsAsync(
            [
                new EducationDocumentTagUpdate(
                    document.Id,
                    document.EntityType,
                    document.EntityId,
                    resolvedTagIds,
                    resolvedTagTitles)
            ],
            _options.ImportBatchSize,
            cancellationToken);
    }

    public async Task<UnitResult<Error>> CreatePendingOrUpdateExistingTagsAsync(
        EntityType entityType,
        Guid entityId,
        IReadOnlyList<Guid> tagIds,
        IReadOnlyList<string> tagTitles,
        CancellationToken cancellationToken = default)
    {
        EducationDocument pending = EducationDocument.CreatePending(
            entityType,
            entityId,
            tagIds,
            tagTitles);

        UnitResult<Error> createResult = await _searchProvider.CreateAsync(
            CollectionNames.EDUCATION_SEARCH,
            pending,
            cancellationToken);

        if (createResult.IsSuccess)
        {
            return UnitResult.Success<Error>();
        }

        bool documentAlreadyExists = createResult.Error.Messages.Any(
            static message => string.Equals(
                message.Code,
                "search.typesense.conflict",
                StringComparison.Ordinal));
        if (!documentAlreadyExists)
        {
            return createResult.Error;
        }

        // A lifecycle event may have created the complete document after the tag handler's
        // initial read. Update only tag-owned fields so the pending placeholder cannot
        // overwrite title, access or placement from that concurrent lifecycle event.
        return await UpdateTagsAsync(
            [
                new EducationDocumentTagUpdate(
                    pending.Id,
                    entityType,
                    entityId,
                    tagIds,
                    tagTitles)
            ],
            _options.ImportBatchSize,
            cancellationToken);
    }

    public async Task<UnitResult<Error>> RefreshDocumentsMatchingTagsAsync(
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        if (tagIds.Count == 0)
        {
            return UnitResult.Success<Error>();
        }

        string filterBy = $"tag_ids:=[{string.Join(",", tagIds.Distinct().Select(static id => $"`{id:D}`"))}]";
        var documents = new List<EducationDocument>(Constants.TAG_SYNC_BATCH_SIZE);
        int page = 1;

        while (true)
        {
            if (page > Constants.TAG_SYNC_MAX_PAGES)
            {
                // Tag affects >250k docs. Returning Error would gun the Wolverine
                // handler into infinite DLQ churn (same tag → same page limit on every
                // retry). Log + ack instead — the leftover tail is recovered by the
                // next manual reindex sweep. Tag canonicalization only changes title
                // text, not access, so eventual consistency is acceptable.
                _logger.LogWarning(
                    "Tag sync page-limit reached. Processed {ProcessedCount} documents across {MaxPages} pages "
                    + "for {TagCount} tags. Tail will reconcile on next full reindex.",
                    documents.Count,
                    Constants.TAG_SYNC_MAX_PAGES,
                    tagIds.Count);
                break;
            }

            Result<SearchResponse<EducationDocument>, Error> documentsResult =
                await SearchAsync(
                    new SearchRequest("*", page, Constants.TAG_SYNC_FETCH_PAGE_SIZE),
                    "title",
                    filterBy,
                    includeFields: Constants.TAG_SYNC_INCLUDE_FIELDS,
                    limitHits: Constants.TAG_SYNC_FETCH_PAGE_SIZE * Constants.TAG_SYNC_MAX_PAGES,
                    cancellationToken: cancellationToken);

            if (documentsResult.IsFailure)
            {
                return documentsResult.Error;
            }

            SearchResponse<EducationDocument> searchResponse = documentsResult.Value;
            if (searchResponse.Hits.Count == 0)
            {
                break;
            }

            documents.AddRange(searchResponse.Hits.Select(static hit => hit.Document));
            page++;
        }

        foreach (EducationDocument[] documentBatch in documents.Chunk(Constants.TAG_SYNC_BATCH_SIZE))
        {
            EntityTagsSearchLookupBatchItem[] entities = documentBatch
                .Select(static document => new EntityTagsSearchLookupBatchItem(document.EntityType, document.EntityId))
                .Distinct()
                .ToArray();

            Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error> lookupResult =
                await _tagServiceClient.GetEntitiesTagsSearchLookupAsync(entities, cancellationToken);

            if (lookupResult.IsFailure)
            {
                return lookupResult.Error;
            }

            IReadOnlyDictionary<(EntityType EntityType, Guid EntityId), EntityTagsSearchLookupBatchDto> tagsByDocument =
                lookupResult.Value.ToDictionary(static item => (item.EntityType, item.EntityId));

            EducationDocumentTagUpdate[] updates = documentBatch
                .Select(document =>
                {
                    tagsByDocument.TryGetValue(
                        (document.EntityType, document.EntityId),
                        out EntityTagsSearchLookupBatchDto? tags);

                    Guid[] resolvedTagIds = tags?.TagIds.Distinct().ToArray() ?? [];
                    string[] resolvedTagTitles = tags?.TagTitles.Distinct(StringComparer.Ordinal).ToArray() ?? [];

                    return new EducationDocumentTagUpdate(
                        document.Id,
                        document.EntityType,
                        document.EntityId,
                        resolvedTagIds,
                        resolvedTagTitles);
                })
                .ToArray();

            UnitResult<Error> updateResult = await UpdateTagsAsync(
                updates,
                Constants.TAG_SYNC_BATCH_SIZE,
                cancellationToken);

            if (updateResult.IsFailure)
            {
                return updateResult.Error;
            }
        }

        return UnitResult.Success<Error>();
    }
}
