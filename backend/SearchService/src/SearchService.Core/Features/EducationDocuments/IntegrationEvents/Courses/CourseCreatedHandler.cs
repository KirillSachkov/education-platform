using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

public sealed class CourseCreatedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CourseCreatedHandler> _logger;

    public CourseCreatedHandler(
        ILogger<CourseCreatedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CourseCreated message, CancellationToken cancellationToken)
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
        UnitResult<Error> upsertResult = await _educationDocumentService.UpsertPreservingTagsAsync(document, cancellationToken);
        if (upsertResult.IsFailure)
        {
            Exception exception = upsertResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to upsert course document into search index. CourseId: {CourseId}",
                message.CourseId);
            throw exception;
        }
    }
}



