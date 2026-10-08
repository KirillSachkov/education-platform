using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

public sealed class CourseHardDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly ILogger<CourseHardDeletedHandler> _logger;

    public CourseHardDeletedHandler(
        ILogger<CourseHardDeletedHandler> logger,
        EducationDocumentService educationDocumentService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
    }

    public async Task Handle(CourseHardDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> deleteResult = await _educationDocumentService.DeleteByIdAsync(
            EducationDocument.CreateCourseId(message.CourseId),
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            Exception exception = deleteResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to delete course document from search index. CourseId: {CourseId}",
                message.CourseId);
            throw exception;
        }
    }
}

