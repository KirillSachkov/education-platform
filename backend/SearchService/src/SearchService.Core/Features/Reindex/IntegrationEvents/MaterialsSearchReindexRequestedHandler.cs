using Common;
using EducationContentService.Contracts;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchExport;
using Microsoft.Extensions.Options;
using SearchService.Core.Features.EducationDocuments;
using SearchService.Core.Reindex;
using SearchService.Domain;
using TagService.Contracts.HttpCommunication;
using TagService.Contracts.SearchLookup;

namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed class MaterialsSearchReindexRequestedHandler
{
    private readonly IEducationContentServiceClient _educationServiceClient;
    private readonly ITagServiceClient _tagServiceClient;
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ISearchIndexingConsumerController _consumerController;
    private readonly SearchReindexOptions _options;
    private readonly ILogger<MaterialsSearchReindexRequestedHandler> _logger;

    public MaterialsSearchReindexRequestedHandler(
        IEducationContentServiceClient educationServiceClient,
        ITagServiceClient tagServiceClient,
        EducationDocumentService educationDocumentService,
        ISearchIndexingConsumerController consumerController,
        IOptions<SearchReindexOptions> options,
        ILogger<MaterialsSearchReindexRequestedHandler> logger)
    {
        _educationServiceClient = educationServiceClient;
        _tagServiceClient = tagServiceClient;
        _educationDocumentService = educationDocumentService;
        _consumerController = consumerController;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Handle(MaterialsSearchReindexRequested message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Search reindex started for {Entity}. RequestId: {RequestId}", EntityType.Material, message.RequestId);

        Result<IAsyncDisposable, Error> pauseResult = await _consumerController.PauseAsync(cancellationToken);
        if (pauseResult.IsFailure)
        {
            _logger.LogError(
                "Search reindex failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                EntityType.Material,
                message.RequestId,
                pauseResult.Error);

            throw pauseResult.Error.ToException();
        }

        await using IAsyncDisposable pausedConsumers = pauseResult.Value;

        string? cursor = null;
        int processedDocuments = 0;
        int batches = 0;
        long totalCount = 0;
        string reindexGeneration = Guid.CreateVersion7().ToString("N");

        while (true)
        {
            Result<CursorResponse<SearchExportEntityDto>, Error> exportResult =
                await _educationServiceClient.ExportMaterialSearchEntitiesAsync(
                    cursor,
                    _options.ExportBatchSize,
                    cancellationToken);

            if (exportResult.IsFailure)
            {
                _logger.LogError(
                    "Search reindex failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                    EntityType.Material,
                    message.RequestId,
                    exportResult.Error);

                throw exportResult.Error.ToException();
            }

            CursorResponse<SearchExportEntityDto> exportBatch = exportResult.Value;
            totalCount = exportBatch.TotalCount;

            if (exportBatch.Items.Count == 0)
            {
                break;
            }

            EntityTagsSearchLookupBatchItem[] tagLookupItems = exportBatch.Items
                .Select(static dto => new EntityTagsSearchLookupBatchItem(dto.EntityType, dto.EntityId))
                .Distinct()
                .ToArray();

            Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error> tagsResult =
                await _tagServiceClient.GetEntitiesTagsSearchLookupAsync(tagLookupItems, cancellationToken);

            if (tagsResult.IsFailure)
            {
                _logger.LogError(
                    "Search reindex failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                    EntityType.Material,
                    message.RequestId,
                    tagsResult.Error);

                throw tagsResult.Error.ToException();
            }

            IReadOnlyDictionary<(EntityType EntityType, Guid EntityId), EntityTagsSearchLookupBatchDto> tagsByEntity =
                tagsResult.Value.ToDictionary(static item => (item.EntityType, item.EntityId));

            var documents = new List<EducationDocument>(exportBatch.Items.Count);
            foreach (SearchExportEntityDto dto in exportBatch.Items)
            {
                tagsByEntity.TryGetValue((dto.EntityType, dto.EntityId), out EntityTagsSearchLookupBatchDto? entityTags);

                Result<EducationDocument, Error> documentResult =
                    EducationDocumentFactory.FromExport(dto, entityTags, reindexGeneration);

                if (documentResult.IsFailure)
                {
                    _logger.LogError(
                        "Search reindex failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                        EntityType.Material,
                        message.RequestId,
                        documentResult.Error);

                    throw documentResult.Error.ToException();
                }

                documents.Add(documentResult.Value);
            }

            UnitResult<Error> importResult = await _educationDocumentService.ImportAsync(
                documents,
                _options.ImportBatchSize,
                cancellationToken);

            if (importResult.IsFailure)
            {
                _logger.LogError(
                    "Search reindex failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                    EntityType.Material,
                    message.RequestId,
                    importResult.Error);

                throw importResult.Error.ToException();
            }

            processedDocuments += exportBatch.Items.Count;
            batches++;

            _logger.LogInformation(
                "Search reindex progress for {Entity}: {ProcessedDocuments}/{TotalCount} documents",
                EntityType.Material,
                processedDocuments,
                totalCount);

            if (exportBatch.NextCursor is null)
            {
                break;
            }

            cursor = exportBatch.NextCursor;

            // Пауза между батчами — снижает пиковую нагрузку на EducationContent SQL
            // и на Typesense import thread. На малой базе не заметна, на 100k+ — критично.
            if (_options.DelayBetweenBatchesMs > 0)
            {
                await Task.Delay(_options.DelayBetweenBatchesMs, cancellationToken);
            }
        }

        UnitResult<Error> cleanupResult = await _educationDocumentService.DeleteByFilterAsync(
            $"entity_type:=`{EntityType.Material}` && reindex_generation:!=`{reindexGeneration}`",
            _options.ImportBatchSize,
            cancellationToken);

        if (cleanupResult.IsFailure)
        {
            _logger.LogError(
                "Search reindex cleanup failed for {Entity}. RequestId: {RequestId}, Error: {Error}",
                EntityType.Material,
                message.RequestId,
                cleanupResult.Error);

            throw cleanupResult.Error.ToException();
        }

        _logger.LogInformation(
            "Search reindex completed for {Entity}. RequestId: {RequestId}",
            EntityType.Material,
            message.RequestId);
    }
}
