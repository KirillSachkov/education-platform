using Shared.Messaging.IntegrationEvents.Tags.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Tags;

public sealed class TagsRemovedFromEntityHandler
{
    private readonly EducationDocumentService _educationDocumentService;

    public TagsRemovedFromEntityHandler(EducationDocumentService educationDocumentService)
    {
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(TagsRemovedFromEntity message, CancellationToken cancellationToken)
    {
        UnitResult<Error> syncResult = await _educationDocumentService.RefreshEntityTagsAsync(
            message.EntityType,
            message.EntityId,
            createPendingDocument: false,
            cancellationToken);

        if (syncResult.IsFailure)
        {
            throw syncResult.Error.ToException();
        }
    }
}
