using Common;

namespace EducationContentService.Contracts.ProgressLookup;

public sealed record MaterialResolveRequestItem(
    Guid CourseId,
    EntityReferenceDto Target);
