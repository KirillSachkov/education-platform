namespace ProgressService.Domain.Bookmarks;

public sealed class MaterialBookmark
{
    private MaterialBookmark(Guid userId, Guid courseId, BookmarkEntityReference entityReference)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        CourseId = courseId;
        EntityReference = entityReference;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private MaterialBookmark()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CourseId { get; private set; }

    public BookmarkEntityReference EntityReference { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<MaterialBookmark, Error> Create(Guid userId, Guid courseId, BookmarkEntityReference entityReference)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (courseId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(courseId));
        }

        if (entityReference.Id == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(entityReference.Id));
        }

        return new MaterialBookmark(userId, courseId, entityReference);
    }
}
