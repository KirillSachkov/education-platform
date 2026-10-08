using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Projects;

public sealed class ProjectRestoredHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<ProjectRestoredHandler> _logger;

    public ProjectRestoredHandler(
        ILogger<ProjectRestoredHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(ProjectRestored message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateProjectId(message.ProjectId),
            false,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update project document in search index. ProjectId: {ProjectId}",
                message.ProjectId);
            throw exception;
        }
    }
}
