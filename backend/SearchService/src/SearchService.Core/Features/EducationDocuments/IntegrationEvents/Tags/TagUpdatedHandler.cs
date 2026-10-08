using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagUpdatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagUpdatedHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagUpdated message, CancellationToken cancellationToken)
    {
        UnitResult<Error> syncResult =
            await _educationDocumentService.RefreshDocumentsMatchingTagsAsync([message.TagId], cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
