namespace EducationContentService.Contracts.ProgressLookup;

public sealed record GetCourseProgressBlueprintsRequest(IReadOnlyCollection<Guid> CourseIds);
