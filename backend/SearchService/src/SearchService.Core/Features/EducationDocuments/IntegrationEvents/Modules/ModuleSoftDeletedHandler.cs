using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Modules;

public sealed class ModuleSoftDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<ModuleSoftDeletedHandler> _logger;

    public ModuleSoftDeletedHandler(
        ILogger<ModuleSoftDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(ModuleSoftDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateModuleId(message.ModuleId),
            true,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to soft delete module document in search index. ModuleId: {ModuleId}",
                message.ModuleId);
            throw exception;
        }

        _logger.LogInformation(
            "Module document soft deleted successfully. ModuleId: {ModuleId}",
            message.ModuleId);
    }
}
