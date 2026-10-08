using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

public sealed class CourseUpdatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CourseUpdatedHandler> _logger;

    public CourseUpdatedHandler(
        ILogger<CourseUpdatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CourseUpdated message, CancellationToken cancellationToken)
    {
        Result<CourseSearchLookupDto, Error> getCourseResult =
            await _educationService.GetCourseSearchLookupAsync(message.CourseId, cancellationToken);
        if (getCourseResult.IsFailure)
        {
            Exception exception = getCourseResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to fetch course details for indexing. CourseId: {CourseId}",
                message.CourseId);
            throw exception;
        }

        CourseSearchLookupDto dto = getCourseResult.Value;

        EducationDocument document = EducationDocumentFactory.FromCourse(dto);
        UnitResult<Error> updateResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);
        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update course document in search index. CourseId: {CourseId}",
                message.CourseId);
            throw exception;
        }
    }
}



