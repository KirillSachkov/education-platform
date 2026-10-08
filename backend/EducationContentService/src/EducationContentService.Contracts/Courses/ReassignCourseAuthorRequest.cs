namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Admin/moderator request to transfer course ownership to another author (#587).
/// </summary>
public sealed record ReassignCourseAuthorRequest(Guid NewAuthorId);

/// <summary>
///     Outcome of a course ownership transfer. Lists the materials/quizzes that were NOT
///     moved because they are shared with other courses — those stay under the previous
///     author so the other courses are not affected.
/// </summary>
public sealed record ReassignCourseAuthorResponse(
    Guid CourseId,
    Guid NewAuthorId,
    IReadOnlyList<Guid> SkippedSharedMaterialIds,
    IReadOnlyList<Guid> SkippedSharedQuizIds);
