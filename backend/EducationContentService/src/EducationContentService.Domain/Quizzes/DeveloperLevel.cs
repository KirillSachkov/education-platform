namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Итоговый уровень разработчика по результату level-test'а
///     (см. <see cref="LevelTestConfig.LevelThresholds"/>). Шкала из шести
///     ступеней (#528); пороги задаются конфигом квиза и снапшотятся в попытку,
///     поэтому новая ступень — это член enum + порог в конфиге, без миграции.
///     Сериализуется строкой (UPPER_SNAKE_CASE) в JSONB и API.
/// </summary>
public enum DeveloperLevel
{
    /// <summary>Новичок — самое начало пути.</summary>
    PRE_JUNIOR,

    /// <summary>Начинающий.</summary>
    JUNIOR,

    /// <summary>Уверенный джун, растущий к мидлу.</summary>
    JUNIOR_PLUS,

    /// <summary>Уверенный.</summary>
    MIDDLE,

    /// <summary>Сильный мидл, растущий к сеньору.</summary>
    MIDDLE_PLUS,

    /// <summary>Продвинутый.</summary>
    SENIOR
}
