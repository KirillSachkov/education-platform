using Common;

namespace EducationContentService.Contracts.ProgressLookup;

public sealed record ResolvedMaterialDto(
    Guid CourseId,
    string CourseSlug,
    string CourseTitle,
    EntityReferenceDto Target,
    string Title,
    string? SectionTitle,
    string SectionType);
