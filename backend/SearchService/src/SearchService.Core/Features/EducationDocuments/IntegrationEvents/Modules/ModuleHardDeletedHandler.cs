using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Modules;

public sealed class ModuleHardDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<ModuleHardDeletedHandler> _logger;

    public ModuleHardDeletedHandler(
        ILogger<ModuleHardDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(ModuleHardDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> deleteResult = await _educationDocumentService.DeleteByIdAsync(
            EducationDocument.CreateModuleId(message.ModuleId),
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            Exception exception = deleteResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to hard delete module document in search index. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }

        _logger.LogInformation(
            "Module document hard deleted successfully. ModuleId: {ModuleId}",
            message.ModuleId);
    }
}
