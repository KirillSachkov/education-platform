using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Materials;

public sealed class MaterialSentToDraftHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<MaterialSentToDraftHandler> _logger;

    public MaterialSentToDraftHandler(
        ILogger<MaterialSentToDraftHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(MaterialSentToDraft message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateMaterialId(message.MaterialId),
            true,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update material document in search index. MaterialId: {MaterialId}",
                message.MaterialId);
            throw exception;
        }
    }
}
