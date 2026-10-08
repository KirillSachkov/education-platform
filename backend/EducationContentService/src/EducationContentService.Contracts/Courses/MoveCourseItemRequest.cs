namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Request to move a course item (module or project) within a course.
/// </summary>
/// <param name="AfterSortKey">SortKey of the item to place after. Null = move to beginning.</param>
/// <param name="BeforeSortKey">SortKey of the item to place before. Null = move to end.</param>
public sealed record MoveCourseItemRequest(string? AfterSortKey, string? BeforeSortKey);
