namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Request to move a module item (lesson, etc.) within a module.
/// </summary>
/// <param name="AfterSortKey">SortKey of the item to place after. Null = move to beginning.</param>
/// <param name="BeforeSortKey">SortKey of the item to place before. Null = move to end.</param>
public sealed record MoveModuleItemRequest(string? AfterSortKey, string? BeforeSortKey);
