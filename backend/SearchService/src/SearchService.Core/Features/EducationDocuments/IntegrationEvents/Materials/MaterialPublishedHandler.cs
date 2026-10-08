using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Materials;

public sealed class MaterialPublishedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<MaterialPublishedHandler> _logger;

    public MaterialPublishedHandler(
        ILogger<MaterialPublishedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(MaterialPublished message, CancellationToken cancellationToken)
    {
        Result<MaterialSearchLookupDto, Error> getMaterialResult =
            await _educationService.GetMaterialSearchLookupAsync(message.MaterialId, cancellationToken);
        if (getMaterialResult.IsFailure)
        {
            Exception exception = getMaterialResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch material details for indexing. MaterialId: {MaterialId}",
                message.MaterialId);
            throw exception;
        }

        MaterialSearchLookupDto dto = getMaterialResult.Value;

        EducationDocument document = EducationDocumentFactory.FromMaterial(dto);
        UnitResult<Error> updateResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);
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


