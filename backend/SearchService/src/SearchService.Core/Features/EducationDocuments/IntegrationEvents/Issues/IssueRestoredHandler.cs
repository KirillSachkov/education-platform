using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssueRestoredHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<IssueRestoredHandler> _logger;

    public IssueRestoredHandler(
        ILogger<IssueRestoredHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(IssueRestored message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateIssueId(message.IssueId),
            false,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to restore issue document in search index. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }
    }
}
