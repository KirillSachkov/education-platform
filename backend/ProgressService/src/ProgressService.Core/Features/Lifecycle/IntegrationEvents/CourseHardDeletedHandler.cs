using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Domain.ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Handles a hard-delete of a course. The Redis tag revocation is published to the
///     Wolverine durable outbox before the DB commit — that way a process crash between
///     the commit and Redis write no longer leaks active content-access tags, because
///     Wolverine will replay <see cref="CourseContentAccessRevocationRequested" /> until it succeeds.
/// </summary>
public sealed class CourseHardDeletedHandler
{
    private const string RESOURCE_TYPE_COURSE = "course";

    private readonly ITransactionManager _transactionManager;
    private readonly ICourseEnrollmentRepository _enrollmentRepository;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly IContentGrantRepository _grantRepository;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<CourseHardDeletedHandler> _logger;

    public CourseHardDeletedHandler(
        ITransactionManager transactionManager,
        ICourseEnrollmentRepository enrollmentRepository,
        IMaterialBookmarkRepository bookmarkRepository,
        IContentGrantRepository grantRepository,
        IOutboxService outboxService,
        ILogger<CourseHardDeletedHandler> logger)
    {
        _transactionManager = transactionManager;
        _enrollmentRepository = enrollmentRepository;
        _bookmarkRepository = bookmarkRepository;
        _grantRepository = grantRepository;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task Handle(CourseHardDeleted message, CancellationToken cancellationToken)
    {
        // 1. Load enrolled users so we can revoke their Redis grants.
        Guid courseId = message.CourseId;
        IReadOnlyList<Domain.Enrollments.CourseEnrollment> existingEnrollments =
            await _enrollmentRepository.GetManyByAsync(
                e => e.CourseId == courseId, cancellationToken);

        List<Guid> userIds = existingEnrollments
            .Select(e => e.UserId)
            .Distinct()
            .ToList();

        // 2. Revoke active content grants in PostgreSQL (EF tracked — committed with SaveChanges)
        IReadOnlyList<ContentGrant> activeGrants = await _grantRepository.GetManyByAsync(
            g => g.ResourceType == RESOURCE_TYPE_COURSE
                 && g.ResourceId == courseId
                 && g.RevokedAt == null,
            cancellationToken);

        foreach (ContentGrant grant in activeGrants)
            grant.Revoke();

        // 3. Delete enrollments (cascades to lesson/module/issue progress via FK)
        int enrollments = await _enrollmentRepository.DeleteByCourseIdAsync(
            message.CourseId, cancellationToken);

        // 4. Delete all bookmarks belonging to this course
        int bookmarks = await _bookmarkRepository.DeleteByCourseIdAsync(
            message.CourseId, cancellationToken);

        // 5. BEFORE commit — enqueue the Redis-revocation message into the durable outbox.
        //    Wolverine will deliver it to the local handler after the DB commit succeeds,
        //    and retry on failure. The Redis step is therefore crash-safe.
        if (userIds.Count > 0)
        {
            await _outboxService.PublishAsync(
                new CourseContentAccessRevocationRequested(message.CourseId, userIds));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to commit hard-delete cleanup for course {CourseId}: {Type}",
                message.CourseId, saveResult.Error.Type);
            throw saveResult.Error.ToException();
        }

        _logger.LogInformation(
            "CourseHardDeleted {CourseId}: revoked {Grants} grants, deleted {Enrollments} enrollments, " +
            "{Bookmarks} bookmarks, scheduled Redis revocation for {Users} users",
            message.CourseId,
            activeGrants.Count,
            enrollments,
            bookmarks,
            userIds.Count);
    }
}
