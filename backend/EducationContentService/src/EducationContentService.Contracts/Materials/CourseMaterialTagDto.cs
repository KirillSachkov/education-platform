namespace EducationContentService.Contracts.Materials;

public sealed record CourseMaterialTagDto(
    Guid Id,
    string Title,
    string Slug,
    string Kind);
