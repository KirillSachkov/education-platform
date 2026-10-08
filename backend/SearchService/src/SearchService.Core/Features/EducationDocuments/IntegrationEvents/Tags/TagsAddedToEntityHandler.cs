using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagsAddedToEntityHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagsAddedToEntityHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagsAddedToEntity message, CancellationToken cancellationToken)
    {
        UnitResult<Error> syncResult = await _educationDocumentService.RefreshEntityTagsAsync(
            message.EntityType,
            message.EntityId,
            createPendingDocument: true,
            cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
