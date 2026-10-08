using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Collections;

public sealed class CollectionHardDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<CollectionHardDeletedHandler> _logger;

    public CollectionHardDeletedHandler(
        ILogger<CollectionHardDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(CollectionHardDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> deleteResult = await _educationDocumentService.DeleteByIdAsync(
            EducationDocument.CreateCollectionId(message.CollectionId),
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            Exception exception = deleteResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to delete collection document from search index. CollectionId: {CollectionId}",
                message.CollectionId);
            throw exception;
        }
    }
}
