using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Modules;

public sealed class ModuleRestoredHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<ModuleRestoredHandler> _logger;

    public ModuleRestoredHandler(
        ILogger<ModuleRestoredHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(ModuleRestored message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateModuleId(message.ModuleId),
            false,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to restore module document in search index. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }

        _logger.LogInformation(
            "Module document restored successfully. ModuleId: {ModuleId}",
            message.ModuleId);
    }
}
