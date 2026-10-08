using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;

namespace ProgressService.Core.Services;

/// <inheritdoc cref="IEnrollmentAnchorService"/>
public sealed class EnrollmentAnchorService : IEnrollmentAnchorService
{
    private readonly ICourseEnrollmentRepository _enrollments;
    private readonly ITransactionManager _transactions;

    public EnrollmentAnchorService(
        ICourseEnrollmentRepository enrollments,
        ITransactionManager transactions)
    {
        _enrollments = enrollments;
        _transactions = transactions;
    }

    public async Task<Result<CourseEnrollment, Error>> EnsureEnrollmentAsync(
        Guid userId,
        Guid courseId,
        Guid authorId,
        EnrollmentSource source,
        CancellationToken cancellationToken = default)
    {
        // Idempotent: return the existing row unchanged. The lazy anchor never re-activates /
        // archives — revoke is handled at the Redis layer (AccessService), progress rows stay
        // as history.
        Result<CourseEnrollment, Error> existing = await _enrollments.GetByAsync(
            e => e.UserId == userId && e.CourseId == courseId, cancellationToken);
        if (existing.IsSuccess)
        {
            return existing.Value;
        }

        Result<CourseEnrollment, Error> create = CourseEnrollment.CreateAnchor(
            userId, courseId, authorId, source);
        if (create.IsFailure)
        {
            return create.Error;
        }

        // Aggregate root via DbSet.AddAsync — EF forces state=Added even though the PK is already
        // set by Guid.CreateVersion7() in the factory (docs/agents/backend-transactions.md rule 4).
        await _enrollments.AddAsync(create.Value, cancellationToken);

        // Flush the anchor now so subsequent same-transaction lookups can see it by FK (e.g.
        // the XP/cascade chain resolves the enrollment via DB GetByAsync, which does not see
        // unsaved Added entities). This runs inside the ambient transaction without committing it.
        // CreateAnchor raises no domain event, so this SaveChanges does not re-dispatch anything.
        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        return create.Value;
    }
}
