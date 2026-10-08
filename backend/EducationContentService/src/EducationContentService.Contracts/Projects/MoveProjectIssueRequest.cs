namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Request to move an issue within a project.
/// </summary>
/// <param name="AfterSortKey">SortKey of the item to place after. Null = move to beginning.</param>
/// <param name="BeforeSortKey">SortKey of the item to place before. Null = move to end.</param>
public sealed record MoveProjectIssueRequest(string? AfterSortKey, string? BeforeSortKey);
