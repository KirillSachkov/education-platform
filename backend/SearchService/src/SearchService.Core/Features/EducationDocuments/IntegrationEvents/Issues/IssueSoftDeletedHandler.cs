using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Issues;

public sealed class IssueSoftDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<IssueSoftDeletedHandler> _logger;

    public IssueSoftDeletedHandler(
        ILogger<IssueSoftDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(IssueSoftDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateIssueId(message.IssueId),
            true,
            cancellationToken);

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
