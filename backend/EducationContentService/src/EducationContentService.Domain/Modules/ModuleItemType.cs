namespace EducationContentService.Domain.Modules;

/// <summary>
///     Тип элемента, прикреплённого к модулю через <see cref="ModuleItem"/>.
/// </summary>
public enum ModuleItemType
{
    /// <summary>Тест / квиз.</summary>
    Quiz,

    /// <summary>Практическая задача.</summary>
    Issue,

    /// <summary>Учебный материал (унифицированный Lesson + Article).</summary>
    Material
}
