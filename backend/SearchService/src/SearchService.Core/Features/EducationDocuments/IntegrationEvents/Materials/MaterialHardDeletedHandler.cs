using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Materials;

public sealed class MaterialHardDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<MaterialHardDeletedHandler> _logger;

    public MaterialHardDeletedHandler(
        ILogger<MaterialHardDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(MaterialHardDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> deleteResult = await _educationDocumentService.DeleteByIdAsync(
            EducationDocument.CreateMaterialId(message.MaterialId),
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            Exception exception = deleteResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to delete material document from search index. MaterialId: {MaterialId}",
                message.MaterialId);
            throw exception;
        }
    }
}
