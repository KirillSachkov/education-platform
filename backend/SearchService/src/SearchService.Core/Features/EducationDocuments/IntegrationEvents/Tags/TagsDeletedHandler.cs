using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagsDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagsDeletedHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagsDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> syncResult =
            await _educationDocumentService.RefreshDocumentsMatchingTagsAsync(message.TagIds, cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
