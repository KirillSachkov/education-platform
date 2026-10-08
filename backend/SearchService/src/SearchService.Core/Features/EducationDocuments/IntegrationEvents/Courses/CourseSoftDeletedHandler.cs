using EducationContentService.Contracts.HttpCommunication;
using SearchService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace SearchService.Core.Features.EducationDocuments.IntegrationEvents.Courses;

public sealed class CourseSoftDeletedHandler
{
    private readonly EducationDocumentService _educationDocumentService;
    private readonly IEducationContentServiceClient _educationService;
    private readonly ILogger<CourseSoftDeletedHandler> _logger;

    public CourseSoftDeletedHandler(
        ILogger<CourseSoftDeletedHandler> logger,
        EducationDocumentService educationDocumentService,
        IEducationContentServiceClient educationService)
    {
        _logger = logger;
        _educationDocumentService = educationDocumentService;
        _educationService = educationService;
    }

    public async Task Handle(CourseSoftDeleted message, CancellationToken cancellationToken)
    {
        UnitResult<Error> updateResult = await _educationDocumentService.SetDeletedAsync(
            EducationDocument.CreateCourseId(message.CourseId),
            true,
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

        // Каскад #378: материал архивированного курса остаётся PUBLISHED, но должен
        // исчезнуть из выдачи, если этот курс — его единственный active-курс. Решение
        // принимает ECS-lookup (IsCourseOrphaned) — здесь только триггер пере-индекса.
        await CourseMaterialsReindexer.ReindexAsync(
            message.CourseId, _educationService, _educationDocumentService, _logger, cancellationToken);
    }
}
