using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagsMergedHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagsMergedHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagsMerged message, CancellationToken cancellationToken)
    {
        Guid[] affectedTagIds =
        [
            message.CanonicalTagId,
            .. message.AliasTagIds
        ];

        UnitResult<Error> syncResult =
            await _educationDocumentService.RefreshDocumentsMatchingTagsAsync(affectedTagIds, cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
