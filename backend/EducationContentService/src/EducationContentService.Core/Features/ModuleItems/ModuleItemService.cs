using EducationContentService.Domain.Modules;
using Ordering;

namespace EducationContentService.Core.Features.ModuleItems;

public sealed class ModuleItemService
{
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly OrderingService<ModuleItem> _ordering;
    private readonly ILogger<ModuleItemService> _logger;

    public ModuleItemService(
        IModuleItemsRepository moduleItemsRepository,
        OrderingService<ModuleItem> ordering,
        ILogger<ModuleItemService> logger)
    {
        _moduleItemsRepository = moduleItemsRepository;
        _ordering = ordering;
        _logger = logger;
    }

    /// <summary>
    ///     Creates a new <see cref="ModuleItem" /> with the correct SortKey (appended to the end),
    ///     adds it to the repository and returns the created entity.
    /// </summary>
    public async Task<Result<ModuleItem, Error>> CreateAsync(
        Guid moduleId,
        ModuleItemType itemType,
        Guid referenceId,
        CancellationToken cancellationToken,
        ViewPriority viewPriority = ViewPriority.Recommended)
    {
        // Invariant: an Issue can belong to only one module.
        if (itemType == ModuleItemType.Issue)
        {
            UnitResult<Error> checkResult =
                await _moduleItemsRepository.CheckIssueNotAttachedAsync(referenceId, cancellationToken);
            if (checkResult.IsFailure)
                return checkResult.Error;
        }

        SortKey sortKey = await _ordering.ComputeAppendSortKeyAsync(
            i => i.ModuleId == moduleId, cancellationToken);

        var item = new ModuleItem(moduleId, itemType, referenceId, sortKey, isOptional: false, viewPriority);

        await _moduleItemsRepository.AddAsync(item, cancellationToken);

        _logger.LogInformation(
            "Item {ReferenceId} ({ItemType}) bound to module {ModuleId}",
            referenceId, itemType, moduleId);

        return item;
    }

    /// <summary>
    ///     Computes a new SortKey to place item between neighbors (for move operations).
    /// </summary>
    public async Task<Result<SortKey, Error>> ComputeMoveSortKey(
        Guid moduleId,
        Guid referenceId,
        string? afterSortKey,
        string? beforeSortKey,
        CancellationToken cancellationToken)
    {
        return await _ordering.ComputeMoveSortKeyAsync(
            i => i.ModuleId == moduleId,
            afterSortKey, beforeSortKey, cancellationToken);
    }

    /// <summary>
    ///     Computes a SortKey for appending an item to the end of a module's list.
    ///     Used by TransferModuleItem when positioning is not explicitly specified.
    /// </summary>
    public async Task<SortKey> ComputeAppendSortKeyAsync(
        Guid moduleId, CancellationToken cancellationToken)
    {
        return await _ordering.ComputeAppendSortKeyAsync(
            i => i.ModuleId == moduleId, cancellationToken);
    }
}
