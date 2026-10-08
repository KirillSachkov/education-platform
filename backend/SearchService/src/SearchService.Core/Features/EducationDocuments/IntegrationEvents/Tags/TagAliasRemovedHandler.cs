using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagAliasRemovedHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagAliasRemovedHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagAliasRemoved message, CancellationToken cancellationToken)
    {
        UnitResult<Error> syncResult =
            await _educationDocumentService.RefreshDocumentsMatchingTagsAsync(message.RemovedAliasTagIds, cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
