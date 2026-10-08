using Common;
using EducationContentService.Contracts;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchExport;
using Microsoft.Extensions.Options;
using SearchService.Contracts;
using SearchService.Core.Features.EducationDocuments;
using SearchService.Core.Reindex;
using SearchService.Core.Reindex.State;
using SearchService.Domain;
using TagService.Contracts.HttpCommunication;
using TagService.Contracts.SearchLookup;

namespace SearchService.Core.Features.Reindex.IntegrationEvents;

public sealed class FullSearchReindexRequestedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ISearchIndexingConsumerController _consumerController;
    private readonly IEducationContentServiceClient _educationServiceClient;
    private readonly ITagServiceClient _tagServiceClient;
    private readonly ISearchReindexStateRepository _reindexStateRepository;
    private readonly SearchReindexOptions _options;
    private readonly ISearchSchemaVersionProvider _schemaVersion;
    private readonly ILogger<FullSearchReindexRequestedHandler> _logger;

    public FullSearchReindexRequestedHandler(
        EducationDocumentService educationDocumentService,
        ISearchIndexingConsumerController consumerController,
        IEducationContentServiceClient educationServiceClient,
        ITagServiceClient tagServiceClient,
        ISearchReindexStateRepository reindexStateRepository,
        IOptions<SearchReindexOptions> options,
        ISearchSchemaVersionProvider schemaVersion,
        ILogger<FullSearchReindexRequestedHandler> logger)
    {
        _educationDocumentService = educationDocumentService;
        _consumerController = consumerController;
        _educationServiceClient = educationServiceClient;
        _tagServiceClient = tagServiceClient;
        _reindexStateRepository = reindexStateRepository;
        _options = options.Value;
        _schemaVersion = schemaVersion;
        _logger = logger;
    }

    public async Task Handle(FullSearchReindexRequested message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Full search reindex started. RequestId: {RequestId}", message.RequestId);

        DateTime startedAtUtc = DateTime.UtcNow;

        Result<IAsyncDisposable, Error> pauseResult = await _consumerController.PauseAsync(cancellationToken);
        if (pauseResult.IsFailure)
        {
            _logger.LogError(
                "Full search reindex failed. RequestId: {RequestId}, Error: {Error}",
                message.RequestId,
                pauseResult.Error);

            throw pauseResult.Error.ToException();
        }

        SearchReindexResponse completedReindex;
        await using (IAsyncDisposable pausedConsumers = pauseResult.Value)
        {
            Result<SearchReindexResponse, Error> result = await _educationDocumentService.RebuildAsync<SearchReindexResponse>(
                async ct =>
                {
                    string? cursor = null;
                    int processedDocuments = 0;
                    int batches = 0;
                    var processedByEntity = new Dictionary<EntityType, int>();
                    var batchesByEntity = new Dictionary<EntityType, int>();

                    while (true)
                    {
                        Result<CursorResponse<SearchExportEntityDto>, Error> exportResult =
                            await _educationServiceClient.ExportAllSearchEntitiesAsync(
                                cursor,
                                _options.ExportBatchSize,
                                ct);

                        if (exportResult.IsFailure)
                        {
                            return exportResult.Error;
                        }

                        CursorResponse<SearchExportEntityDto> exportBatch = exportResult.Value;

                        if (exportBatch.Items.Count == 0)
                        {
                            break;
                        }

                        EntityTagsSearchLookupBatchItem[] tagLookupItems = exportBatch.Items
                            .Select(static dto => new EntityTagsSearchLookupBatchItem(dto.EntityType, dto.EntityId))
                            .Distinct()
                            .ToArray();

                        Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error> tagsResult =
                            await _tagServiceClient.GetEntitiesTagsSearchLookupAsync(tagLookupItems, ct);

                        if (tagsResult.IsFailure)
                        {
                            return tagsResult.Error;
                        }

                        IReadOnlyDictionary<(EntityType EntityType, Guid EntityId), EntityTagsSearchLookupBatchDto> tagsByEntity =
                            tagsResult.Value.ToDictionary(static item => (item.EntityType, item.EntityId));

                        var documents = new List<EducationDocument>(exportBatch.Items.Count);
                        foreach (SearchExportEntityDto dto in exportBatch.Items)
                        {
                            tagsByEntity.TryGetValue((dto.EntityType, dto.EntityId), out EntityTagsSearchLookupBatchDto? entityTags);

                            Result<EducationDocument, Error> documentResult = EducationDocumentFactory.FromExport(dto, entityTags);
                            if (documentResult.IsFailure)
                            {
                                return documentResult.Error;
                            }

                            documents.Add(documentResult.Value);
                        }

                        UnitResult<Error> importResult = await _educationDocumentService.ImportAsync(
                            documents,
                            _options.ImportBatchSize,
                            ct);

                        if (importResult.IsFailure)
                        {
                            return importResult.Error;
                        }

                        foreach (IGrouping<EntityType, SearchExportEntityDto> entityGroup in exportBatch.Items.GroupBy(static dto => dto.EntityType))
                        {
                            processedByEntity[entityGroup.Key] = processedByEntity.GetValueOrDefault(entityGroup.Key) + entityGroup.Count();
                            batchesByEntity[entityGroup.Key] = batchesByEntity.GetValueOrDefault(entityGroup.Key) + 1;
                        }

                        processedDocuments += documents.Count;
                        batches++;

                        if (exportBatch.NextCursor is null)
                        {
                            break;
                        }

                        cursor = exportBatch.NextCursor;

                        if (_options.DelayBetweenBatchesMs > 0)
                        {
                            await Task.Delay(_options.DelayBetweenBatchesMs, cancellationToken);
                        }
                    }

                    SearchReindexEntityResult[] results = processedByEntity
                        .OrderBy(static item => item.Key)
                        .Select(item => new SearchReindexEntityResult(
                            item.Key,
                            item.Value,
                            batchesByEntity.GetValueOrDefault(item.Key),
                            item.Value))
                        .ToArray();

                    return new SearchReindexResponse(
                        EntityType: null,
                        ProcessedDocuments: processedDocuments,
                        Batches: batches,
                        Entities: results,
                        StartedAtUtc: startedAtUtc,
                        CompletedAtUtc: DateTime.UtcNow);
                },
                cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogError(
                    "Full search reindex failed. RequestId: {RequestId}, Error: {Error}",
                    message.RequestId,
                    result.Error);

                throw result.Error.ToException();
            }

            completedReindex = result.Value;
        }

        // Mark applied only after consumers were successfully resumed. A resume failure
        // escapes DisposeAsync and Wolverine retries the reindex instead of recording a
        // healthy generation while both indexing queues remain paused.
        // На успешном завершении расписываем applied_generation/schema_hash/deploy_stamp
        // текущими значениями кода/конфига. Если handler упал по пути — state НЕ обновляется,
        // и следующий старт процесса увидит mismatch и попробует снова. Это намеренная
        // самовосстанавливаемость. Пустой DeployStamp (dev/local) нормализуем в null.
        DateTime completedAtUtc = DateTime.UtcNow;
        await _reindexStateRepository.MarkAppliedAsync(
            _options.ReindexGeneration,
            _schemaVersion.SchemaHash,
            string.IsNullOrEmpty(_options.DeployStamp) ? null : _options.DeployStamp,
            message.RequestId,
            completedAtUtc,
            cancellationToken);

        _logger.LogInformation(
            "Full search reindex completed. RequestId: {RequestId}, AppliedGeneration: {Generation}, ProcessedDocuments: {ProcessedDocuments}, Batches: {Batches}",
            message.RequestId,
            _options.ReindexGeneration,
            completedReindex.ProcessedDocuments,
            completedReindex.Batches);
    }
}
