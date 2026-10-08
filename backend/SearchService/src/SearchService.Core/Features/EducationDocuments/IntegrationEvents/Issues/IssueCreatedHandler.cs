using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssueCreatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<IssueCreatedHandler> _logger;

    public IssueCreatedHandler(
        ILogger<IssueCreatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(IssueCreated message, CancellationToken cancellationToken)
    {
        Result<IssueSearchLookupDto, Error> getIssueResult =
            await _educationService.GetIssueSearchLookupAsync(message.IssueId, cancellationToken);

        if (getIssueResult.IsFailure)
        {
            Exception exception = getIssueResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch issue details for indexing. IssueId: {IssueId}, ModuleId: {ModuleId}",
                message.IssueId,
                message.ModuleId);
            throw exception;
        }

        EducationDocument document = EducationDocumentFactory.FromIssue(getIssueResult.Value);
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to upsert issue document into search index. IssueId: {IssueId}, ModuleId: {ModuleId}",
                message.IssueId,
                message.ModuleId);
            throw exception;
        }

        _logger.LogInformation(
            "Issue document indexed successfully. IssueId: {IssueId}, ModuleId: {ModuleId}",
            message.IssueId,
            getIssueResult.Value.ModuleId ?? message.ModuleId);
    }
}
