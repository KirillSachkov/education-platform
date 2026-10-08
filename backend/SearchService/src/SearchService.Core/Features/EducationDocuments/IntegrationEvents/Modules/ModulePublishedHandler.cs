using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Modules;

public sealed class ModulePublishedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<ModulePublishedHandler> _logger;

    public ModulePublishedHandler(
        ILogger<ModulePublishedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(ModulePublished message, CancellationToken cancellationToken)
    {
        Result<ModuleSearchLookupDto, Error> getModuleResult =
            await _educationService.GetModuleSearchLookupAsync(message.ModuleId, cancellationToken);

        if (getModuleResult.IsFailure)
        {
            Exception exception = getModuleResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch module details for indexing. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }

        EducationDocument document = EducationDocumentFactory.FromModule(getModuleResult.Value);
        UnitResult<Error> updateResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update module document in search index. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }
    }
}
