using SharedKernel.DomainEvents;

namespace ProgressService.Domain.CoursePositions;

/// <summary>
/// Last opened material/issue per (User, Course). User-scoped трекер «продолжить с того места,
/// где остановился» — отличается от <c>MaterialView</c> (просмотрено) тем, что фиксируется
/// при ОТКРЫТИИ урока/задания, а не при завершении. Один ряд на пару (UserId, CourseId);
/// каждое последующее открытие перезаписывает <see cref="EntityType"/>/<see cref="EntityId"/>
/// и поднимает <see cref="OpenedAt"/>.
/// </summary>
public sealed class CoursePosition : AggregateRoot
{
    public const string ENTITY_TYPE_MATERIAL = "MATERIAL";
    public const string ENTITY_TYPE_ISSUE = "ISSUE";

    private CoursePosition(Guid userId, Guid courseId, string entityType, Guid entityId)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        CourseId = courseId;
        EntityType = entityType;
        EntityId = entityId;
        OpenedAt = DateTime.UtcNow;
        CreatedAt = OpenedAt;
    }

    private CoursePosition()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CourseId { get; private set; }

    public string EntityType { get; private set; } = ENTITY_TYPE_MATERIAL;

    public Guid EntityId { get; private set; }

    public DateTime OpenedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Result<CoursePosition, Error> Create(
        Guid userId, Guid courseId, string entityType, Guid entityId)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (courseId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(courseId));
        }

        if (entityId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(entityId));
        }

        if (!IsKnownEntityType(entityType))
        {
            return GeneralErrors.ValueIsInvalid(nameof(entityType));
        }

        return new CoursePosition(userId, courseId, entityType, entityId);
    }

    public UnitResult<Error> Touch(string entityType, Guid entityId)
    {
        if (entityId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(entityId));
        }

        if (!IsKnownEntityType(entityType))
        {
            return GeneralErrors.ValueIsInvalid(nameof(entityType));
        }

        EntityType = entityType;
        EntityId = entityId;
        OpenedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    private static bool IsKnownEntityType(string entityType) =>
        string.Equals(entityType, ENTITY_TYPE_MATERIAL, StringComparison.Ordinal)
        || string.Equals(entityType, ENTITY_TYPE_ISSUE, StringComparison.Ordinal);
}
