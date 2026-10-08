namespace EducationContentService.Domain;

/// <summary>
///     Статус публикации элемента образовательного контента.
/// </summary>
public enum PublicationStatus
{
    /// <summary>Черновик — не виден студентам.</summary>
    DRAFT,

    /// <summary>Опубликован — доступен студентам.</summary>
    PUBLISHED,

    /// <summary>Архивирован — скрыт из каталога.</summary>
    ARCHIVED
}
