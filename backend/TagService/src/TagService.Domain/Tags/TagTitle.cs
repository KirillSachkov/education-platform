namespace TagService.Domain.Tags;

/// <summary>
/// Заголовок тега / Tag title.
/// </summary>
public record TagTitle
{
    /// <summary>
    /// Минимальная длина заголовка / Minimum title length.
    /// </summary>
    public const int MIN_LENGTH = 1;

    /// <summary>
    /// Максимальная длина заголовка / Maximum title length.
    /// </summary>
    public const int MAX_LENGTH = 150;

    private TagTitle(string value) => Value = value;

    /// <summary>
    /// Значение заголовка / Title value.
    /// </summary>
    public string Value { get; private set; }

    /// <summary>
    /// Создает заголовок тега с валидацией / Creates a tag title with validation.
    /// </summary>
    /// <param name="value">Значение заголовка / Title value.</param>
    /// <returns>Результат создания заголовка / Result of title creation.</returns>
    public static Result<TagTitle, Error> Of(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
            return GeneralErrors.ValueIsRequired("tag.title");

        if (value.Length is > MAX_LENGTH or < MIN_LENGTH)
            return GeneralErrors.ValueIsInvalid("tag.title");

        return new TagTitle(value);
    }
}
