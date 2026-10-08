using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Collections;

public sealed class CollectionAccessChangedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CollectionAccessChangedHandler> _logger;

    public CollectionAccessChangedHandler(
        ILogger<CollectionAccessChangedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CollectionAccessChanged message, CancellationToken cancellationToken)
    {
        Result<CollectionSearchLookupDto, Error> getCollectionResult =
            await _educationService.GetCollectionSearchLookupAsync(message.CollectionId, cancellationToken);
        if (getCollectionResult.IsFailure)
        {
            Exception exception = getCollectionResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch collection details for access re-indexing. CollectionId: {CollectionId}",
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
                "Failed to refresh collection access tags in search index. CollectionId: {CollectionId}",
                message.CollectionId);
            throw exception;
        }
    }
}
