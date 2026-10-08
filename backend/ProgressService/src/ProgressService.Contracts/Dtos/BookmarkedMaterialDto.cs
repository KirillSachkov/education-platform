using Common;

namespace ProgressService.Contracts.Dtos;

public sealed record BookmarkedMaterialDto(
    Guid CourseId,
    string CourseSlug,
    string CourseTitle,
    EntityReferenceDto Target,
    string Title,
    string? SectionTitle,
    string SectionType,
    DateTime CreatedAt,
    bool IsAccessible,
    string? LockReason);
