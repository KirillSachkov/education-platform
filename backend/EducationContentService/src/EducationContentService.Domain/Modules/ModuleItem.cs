using Ordering;

namespace EducationContentService.Domain.Modules;

/// <summary>
///     Join-сущность для связи модуля с атомарным контентом (Lesson, Article, Quiz, Issue).
///     Содержит позицию (SortKey) и флаг опциональности.
/// </summary>
public sealed class ModuleItem : IOrderedItem
{
    public ModuleItem(
        Guid moduleId,
        ModuleItemType itemType,
        Guid referenceId,
        SortKey sortKey,
        bool isOptional,
        ViewPriority viewPriority = ViewPriority.Recommended)
    {
        Id = Guid.CreateVersion7();
        ModuleId = moduleId;
        ItemType = itemType;
        ReferenceId = referenceId;
        SortKey = sortKey;
        IsOptional = isOptional;
        ViewPriority = viewPriority;
    }

    // EF Core
    private ModuleItem()
    {
    }

    public Guid Id { get; }

    public Guid ModuleId { get; }

    public ModuleItemType ItemType { get; }

    public Guid ReferenceId { get; }

    public SortKey SortKey { get; private set; } = null!;

    public bool IsOptional { get; private set; }

    public ViewPriority ViewPriority { get; private set; }

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;

    public void UpdateIsOptional(bool isOptional) => IsOptional = isOptional;

    public void UpdateViewPriority(ViewPriority viewPriority) => ViewPriority = viewPriority;
}
