using Common;

namespace ProgressService.Domain.Bookmarks;

public sealed record BookmarkEntityReference : EntityReference
{
    private BookmarkEntityReference(EntityType type, Guid id)
        : base(type, id)
    {
    }

    public static bool IsSupported(EntityType entityType) =>
        entityType is EntityType.Material or EntityType.Issue;

    public static Result<BookmarkEntityReference, Error> Of(EntityType type, Guid id)
    {
        UnitResult<Error> validationResult = ValidateBase(type, id);
        if (validationResult.IsFailure)
        {
            return validationResult.Error;
        }

        if (!IsSupported(type))
        {
            return ProgressErrors.BookmarkTargetTypeNotSupported(type);
        }

        return new BookmarkEntityReference(type, id);
    }
}
