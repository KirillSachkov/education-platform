using EducationContentService.Contracts.HttpCommunication;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

public sealed class CourseRestoredHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CourseRestoredHandler> _logger;

    public CourseRestoredHandler(
        ILogger<CourseRestoredHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CourseRestored message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateCourseId(message.CourseId),
            false,
            cancellationToken);

        if (updateResult.IsFailure)
        {
            Exception exception = updateResult.Error.ToException();
            _logger.LogError(
                exception,
                "Failed to update course document in search index. CourseId: {CourseId}",
                message.CourseId);
            throw exception;
        }

        // Каскад #378: при восстановлении курса его материалы снова должны стать
        // видимыми (если этот курс — их active-курс). Пере-индексируем каждый —
        // GetMaterialSearchLookup пересчитает IsCourseOrphaned (курс уже PUBLISHED).
        await CourseMaterialsReindexer.ReindexAsync(
            message.CourseId, _educationService, _educationDocumentService, _logger, cancellationToken);
    }
}
