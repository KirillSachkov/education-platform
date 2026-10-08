using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Projects;

public sealed class ProjectUpdatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<ProjectUpdatedHandler> _logger;

    public ProjectUpdatedHandler(
        ILogger<ProjectUpdatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(ProjectUpdated message, CancellationToken cancellationToken)
    {
        Result<ProjectSearchLookupDto, Error> getProjectResult =
            await _educationService.GetProjectSearchLookupAsync(message.ProjectId, cancellationToken);

        if (getProjectResult.IsFailure)
        {
            Exception exception = getProjectResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch project details for indexing. ProjectId: {ProjectId}",
                message.ProjectId);
            throw exception;
        }

        EducationDocument document = EducationDocumentFactory.FromProject(getProjectResult.Value);
        UnitResult<Error> updateResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

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
