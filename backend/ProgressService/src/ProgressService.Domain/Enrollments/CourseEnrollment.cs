using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Enrollments;

/// <summary>
/// Корневой агрегат участия пользователя в курсе.
///
/// После access-derive-model (#367): чистый <b>lazy progress-anchor</b>. Доступ полностью
/// определяется через AccessService PlanGrant'ы и Redis plan-теги; эта запись существует только
/// как FK-родитель прогресса (module/issue/project). Создаётся «по требованию» на первом
/// entitled-взаимодействии (<see cref="CreateAnchor"/>) либо как author pre-seed на
/// <c>course.created</c>. Никаких archive/sort-key/source-ref концептов — revoke режется на
/// Redis-уровне, строка остаётся как история.
/// </summary>
public sealed class CourseEnrollment : AggregateRoot
{
    private CourseEnrollment(
        Guid userId,
        Guid courseId,
        Guid authorId,
        EnrollmentSource source)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        CourseId = courseId;
        AuthorId = authorId;
        Source = source;
        EnrolledAt = DateTime.UtcNow;
        CreatedAt = EnrolledAt;
        UpdatedAt = EnrolledAt;
    }

    private CourseEnrollment()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CourseId { get; private set; }

    /// <summary>ID автора курса (владелец курса для проверки прав на ревью).</summary>
    public Guid AuthorId { get; private set; }

    /// <summary>
    ///     Источник зачисления для аудита. См. <see cref="EnrollmentSource"/>. Под derive-model
    ///     новые строки пишутся с <c>ENGAGEMENT</c> (lazy anchor) или <c>AUTHOR_SELF</c> (author
    ///     pre-seed); исторические значения остаются на старых строках.
    /// </summary>
    public EnrollmentSource Source { get; private set; }

    public DateTime EnrolledAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     Silent constructor for the lazily-created progress anchor (access-derive-model).
    ///     Raises <b>no</b> domain/integration event — the row exists purely as the FK parent for
    ///     progress (module/issue/project). Access is governed by AccessService grants, not by
    ///     this row, so there is nothing to signal downstream. Used by <c>EnsureEnrollmentAsync</c>
    ///     on first entitled engagement and for the author pre-seed.
    /// </summary>
    public static Result<CourseEnrollment, Error> CreateAnchor(
        Guid userId,
        Guid courseId,
        Guid authorId,
        EnrollmentSource source)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (courseId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(courseId));
        }

        return new CourseEnrollment(userId, courseId, authorId, source);
    }
}