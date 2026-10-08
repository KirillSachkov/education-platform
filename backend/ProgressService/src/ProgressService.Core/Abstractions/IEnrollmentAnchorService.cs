using ProgressService.Domain.Enrollments;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Ensures a lazily-created <see cref="CourseEnrollment"/> progress anchor exists for a
///     (user, course) pair (access-derive-model Phase 2). The anchor is the FK parent for
///     module/issue/project progress and XP — it holds progress only and does NOT gate access
///     (access lives in AccessService grants + Redis tags).
///
///     <para>Idempotent: returns the existing row if present, otherwise creates a new silent
///     anchor (<see cref="CourseEnrollment.CreateAnchor"/>) via <c>DbSet.AddAsync</c>. No
///     domain/integration event is raised on create.</para>
///
///     <para>The caller is responsible for the entitlement gate before calling this — the service
///     does not check access, it only materializes the anchor. The caller must
///     <c>SaveChangesAsync</c> through <c>ITransactionManager</c> to persist a newly added row.</para>
/// </summary>
public interface IEnrollmentAnchorService
{
    Task<Result<CourseEnrollment, Error>> EnsureEnrollmentAsync(
        Guid userId,
        Guid courseId,
        Guid authorId,
        EnrollmentSource source,
        CancellationToken cancellationToken = default);
}
