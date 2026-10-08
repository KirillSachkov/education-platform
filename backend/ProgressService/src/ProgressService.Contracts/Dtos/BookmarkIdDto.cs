using Common;

namespace ProgressService.Contracts.Dtos;

public sealed record BookmarkIdDto(
    Guid CourseId,
    EntityReferenceDto Target,
    DateTime CreatedAt);
