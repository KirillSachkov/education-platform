using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssueHardDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<IssueHardDeletedHandler> _logger;

    public IssueHardDeletedHandler(
        ILogger<IssueHardDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(IssueHardDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> deleteResult = await _educationDocumentService.DeleteByIdAsync(
            EducationDocument.CreateIssueId(message.IssueId),
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            Exception exception = deleteResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to delete issue document from search index. IssueId: {IssueId}",
                message.IssueId);
            throw exception;
        }
    }
}
