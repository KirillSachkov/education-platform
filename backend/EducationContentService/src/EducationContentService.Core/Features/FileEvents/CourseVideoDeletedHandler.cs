using Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Courses;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.FileEvents;

public sealed class CourseVideoDeletedHandler
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<CourseVideoDeletedHandler> _logger;

    public CourseVideoDeletedHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        ILogger<CourseVideoDeletedHandler> logger)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(FileAssetDeleted message, CancellationToken ct)
    {
        if (!string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.COURSE, StringComparison.Ordinal)
            || !string.Equals(message.UsageType, FileEventsRouting.UsageTypes.COURSE_VIDEO, StringComparison.Ordinal))
            return;

        if (message.TargetEntityId is null)
            return;

        Result<Course, Error> courseResult = await _coursesRepository
            .GetByAsync(c => c.Id == message.TargetEntityId.Value, ct);
        if (courseResult.IsFailure)
        {
            _logger.LogInformation(
                "Course {CourseId} not found for FileAssetDeleted {AssetId} — late event, no-op",
                message.TargetEntityId, message.AssetId);
            return;
        }

        if (courseResult.Value.VideoId?.Value != message.AssetId ||
            courseResult.Value.VideoBindingRevision != message.BindingRevision)
            return;

        courseResult.Value.DetachVideo();
        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(ct);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();
    }
}
