using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Projects;

public sealed class ProjectCreatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<ProjectCreatedHandler> _logger;

    public ProjectCreatedHandler(
        ILogger<ProjectCreatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(ProjectCreated message, CancellationToken cancellationToken)
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
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to upsert project document into search index. ProjectId: {ProjectId}",
                message.ProjectId);
            throw exception;
        }
    }
}
