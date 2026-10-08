using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssueAccessChangedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<IssueAccessChangedHandler> _logger;

    public IssueAccessChangedHandler(
        ILogger<IssueAccessChangedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(IssueAccessChanged message, CancellationToken cancellationToken)
    {
        Result<IssueSearchLookupDto, Error> getIssueResult =
            await _educationService.GetIssueSearchLookupAsync(message.IssueId, cancellationToken);

        if (getIssueResult.IsFailure)
        {
            Exception exception = getIssueResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch issue details for access re-indexing. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }

        EducationDocument document = EducationDocumentFactory.FromIssue(getIssueResult.Value);
        UnitResult<Error> upsertResult =
            await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);

        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to refresh issue access tags in search index. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }
    }
}
