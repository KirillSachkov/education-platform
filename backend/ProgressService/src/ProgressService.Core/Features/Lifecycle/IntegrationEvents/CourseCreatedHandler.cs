using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Auto-enrolls the course author when a course is created, so the author sees their
///     own course as enrolled (badge, leaderboard, navigation parity with students).
///     Idempotent — re-delivery skips if an enrollment already exists.
///
///     <para>access-derive-model Phase 2: pre-seeds the author's progress anchor with
///     <see cref="EnrollmentSource.AUTHOR_SELF"/> via <see cref="IEnrollmentAnchorService"/>.
///     This is a silent create (no <c>CourseEnrolled</c> event) — the anchor holds progress
///     only; the author's access is governed by AccessService grants, not by this row.</para>
/// </summary>
public sealed class CourseCreatedHandler
{
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<CourseCreatedHandler> _logger;

    public CourseCreatedHandler(
        IEnrollmentAnchorService enrollmentAnchorService,
        ITransactionManager transactionManager,
        ILogger<CourseCreatedHandler> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(CourseCreated message, CancellationToken cancellationToken)
    {
        if (message.AuthorId == Guid.Empty)
        {
            _logger.LogWarning(
                "CourseCreated for {CourseId} has empty AuthorId — skipping author auto-enrollment",
                message.CourseId);
            return;
        }

        Result<CourseEnrollment, Error> ensure = await _enrollmentAnchorService.EnsureEnrollmentAsync(
            message.AuthorId,
            message.CourseId,
            message.AuthorId,
            EnrollmentSource.AUTHOR_SELF,
            cancellationToken);
        if (ensure.IsFailure)
        {
            _logger.LogError(
                "Failed to construct author enrollment anchor for course {CourseId}, author {AuthorId}: {Type}",
                message.CourseId, message.AuthorId, ensure.Error.Type);
            throw ensure.Error.ToException();
        }

        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            _logger.LogError(
                "Failed to commit author enrollment for course {CourseId}, author {AuthorId}: {Type}",
                message.CourseId, message.AuthorId, save.Error.Type);
            throw save.Error.ToException();
        }

        _logger.LogInformation(
            "Auto-enrolled author {AuthorId} into course {CourseId}",
            message.AuthorId, message.CourseId);
    }
}
