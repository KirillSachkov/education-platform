using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Collections;

public sealed class CollectionCreatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CollectionCreatedHandler> _logger;

    public CollectionCreatedHandler(
        ILogger<CollectionCreatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CollectionCreated message, CancellationToken cancellationToken)
    {
        Result<CollectionSearchLookupDto, Error> getCollectionResult =
            await _educationService.GetCollectionSearchLookupAsync(message.CollectionId, cancellationToken);
        if (getCollectionResult.IsFailure)
        {
            Exception exception = getCollectionResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch collection details for indexing. CollectionId: {CollectionId}",
                message.CollectionId);
            throw exception;
        }

        CollectionSearchLookupDto dto = getCollectionResult.Value;

        EducationDocument document = EducationDocumentFactory.FromCollection(dto);
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);
        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to upsert collection document into search index. CollectionId: {CollectionId}",
                message.CollectionId);
            throw exception;
        }
    }
}
