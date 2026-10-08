using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Modules;

public sealed class ModuleCreatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<ModuleCreatedHandler> _logger;

    public ModuleCreatedHandler(
        ILogger<ModuleCreatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(ModuleCreated message, CancellationToken cancellationToken)
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
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to upsert module document into search index. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }

        _logger.LogInformation(
            "Module document indexed successfully. ModuleId: {ModuleId}",
            message.ModuleId);
    }
}
