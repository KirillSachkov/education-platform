namespace EducationContentService.Domain.Materials;

/// <summary>
///     Вариант контента <see cref="Material"/> — определяет,
///     в какой форме подаётся материал студенту.
/// </summary>
public enum MaterialKind
{
    /// <summary>Статья — текстовый материал (значение по умолчанию).</summary>
    ARTICLE,

    /// <summary>Видео — основной контент — видеозапись, текст опционален как описание.</summary>
    VIDEO,

    /// <summary>Короткая заметка в Markdown.</summary>
    NOTE,

    /// <summary>Запись стрима / лекции.</summary>
    STREAM
}
