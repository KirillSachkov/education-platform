namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Request to transfer a module item (lesson, issue) to another module.
/// </summary>
/// <param name="TargetModuleId">The module to move the item to.</param>
/// <param name="AfterSortKey">SortKey of the item to place after in target module. Null = use BeforeSortKey or append.</param>
/// <param name="BeforeSortKey">SortKey of the item to place before in target module. Null = use AfterSortKey or append.</param>
public sealed record TransferModuleItemRequest(
    Guid TargetModuleId,
    string? AfterSortKey,
    string? BeforeSortKey);
