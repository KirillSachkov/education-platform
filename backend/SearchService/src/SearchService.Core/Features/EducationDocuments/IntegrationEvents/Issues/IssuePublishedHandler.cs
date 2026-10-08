using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssuePublishedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<IssuePublishedHandler> _logger;

    public IssuePublishedHandler(
        ILogger<IssuePublishedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(IssuePublished message, CancellationToken cancellationToken)
    {
        Result<IssueSearchLookupDto, Error> getIssueResult =
            await _educationService.GetIssueSearchLookupAsync(message.IssueId, cancellationToken);

        if (getIssueResult.IsFailure)
        {
            Exception exception = getIssueResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch issue details for indexing. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }

        EducationDocument document = EducationDocumentFactory.FromIssue(getIssueResult.Value);
        UnitResult<Error> updateResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update issue document in search index. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }
    }
}
