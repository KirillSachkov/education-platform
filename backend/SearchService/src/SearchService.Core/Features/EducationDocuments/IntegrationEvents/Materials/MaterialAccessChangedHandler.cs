using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Materials;

public sealed class MaterialAccessChangedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<MaterialAccessChangedHandler> _logger;

    public MaterialAccessChangedHandler(
        ILogger<MaterialAccessChangedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(MaterialAccessChanged message, CancellationToken cancellationToken)
    {
        Result<MaterialSearchLookupDto, Error> getMaterialResult =
            await _educationService.GetMaterialSearchLookupAsync(message.MaterialId, cancellationToken);
        if (getMaterialResult.IsFailure)
        {
            Exception exception = getMaterialResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch material details for access re-indexing. MaterialId: {MaterialId}",
                message.MaterialId);
            throw exception;
        }

        MaterialSearchLookupDto dto = getMaterialResult.Value;

        EducationDocument document = EducationDocumentFactory.FromMaterial(dto);
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);
        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to refresh material access tags in search index. MaterialId: {MaterialId}",
                message.MaterialId);
            throw exception;
        }
    }
}
