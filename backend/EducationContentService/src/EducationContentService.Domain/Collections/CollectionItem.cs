using Ordering;

namespace EducationContentService.Domain.Collections;

/// <summary>
///     Элемент подборки — отдельная сущность, ссылающаяся на секцию по FK и на контент
///     generic-ссылкой (<see cref="ItemType"/> + <see cref="ReferenceId"/>, зеркало
///     <c>module_items</c>, #491): Material или Quiz. Уникальность
///     <c>(SectionId, ItemType, ReferenceId)</c> гарантируется БД-индексом, а не
///     проверкой в памяти. FK на materials/quizzes нет — существование референса
///     валидирует handler (AddItem), каскады чистят Delete-use-case'ы.
/// </summary>
public sealed class CollectionItem : IOrderedItem
{
    // EF Core constructor
    private CollectionItem() { }

    public CollectionItem(Guid sectionId, CollectionItemType itemType, Guid referenceId, SortKey sortKey)
    {
        Id = Guid.CreateVersion7();
        SectionId = sectionId;
        ItemType = itemType;
        ReferenceId = referenceId;
        SortKey = sortKey;
    }

    public Guid Id { get; private set; }
    public Guid SectionId { get; private set; }
    public CollectionItemType ItemType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public SortKey SortKey { get; private set; } = null!;

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;
}
