namespace EducationContentService.Domain.Collections;

/// <summary>
///     Тип элемента подборки (<see cref="CollectionItem"/>) — generic-ссылка по паттерну
///     <c>module_items</c> (#491). Хранится строкой (UPPER_SNAKE_CASE, см. Enum Storage
///     Convention в backend/CLAUDE.md), новый тип = +1 член enum без CHECK-миграции.
/// </summary>
public enum CollectionItemType
{
    /// <summary>Учебный материал (<c>materials.id</c>).</summary>
    MATERIAL,

    /// <summary>Квиз (<c>quizzes.id</c>, standalone после #489).</summary>
    QUIZ
}
