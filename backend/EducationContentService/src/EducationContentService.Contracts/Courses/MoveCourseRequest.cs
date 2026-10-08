namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Request to move a course within an author's catalog.
/// </summary>
/// <param name="AfterSortKey">SortKey of the course to place after. Null = move to beginning.</param>
/// <param name="BeforeSortKey">SortKey of the course to place before. Null = move to end.</param>
public sealed record MoveCourseRequest(string? AfterSortKey, string? BeforeSortKey);
