using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Projects;

public sealed class ProjectSoftDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<ProjectSoftDeletedHandler> _logger;

    public ProjectSoftDeletedHandler(
        ILogger<ProjectSoftDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(ProjectSoftDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateProjectId(message.ProjectId),
            true,
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
