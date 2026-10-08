using EducationContentService.Domain.Modules;

namespace EducationContentService.Domain.Projects.ValueObjects;

/// <summary>
///     Value Object — внутренний материал задачи со ссылкой на существующий контент (Lesson, Article, Quiz).
///     Позиция в списке определяется индексом в массиве (Issue.InternalMaterials).
/// </summary>
public sealed record IssueInternalMaterial
{
    private IssueInternalMaterial(ModuleItemType itemType, Guid referenceId, bool isRequired)
    {
        ItemType = itemType;
        ReferenceId = referenceId;
        IsRequired = isRequired;
    }

    public ModuleItemType ItemType { get; }

    public Guid ReferenceId { get; }

    public bool IsRequired { get; }

    public static Result<IssueInternalMaterial, Error> Create(ModuleItemType itemType, Guid referenceId,
        bool isRequired)
    {
        if (referenceId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(referenceId));

        return new IssueInternalMaterial(itemType, referenceId, isRequired);
    }
}
