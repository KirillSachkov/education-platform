namespace CommentService.Domain;

/// <summary>
/// Представляет содержимое комментария / Represents comment content.
/// </summary>
public sealed record Content
{
    /// <summary>
    /// Максимальная длина содержимого / Maximum content length.
    /// </summary>
    public const int MAX_LENGTH = 1000;

    /// <summary>
    /// Значение содержимого / Content value.
    /// </summary>
    public string Value { get; }

    private Content(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Создает новый экземпляр Content с валидацией / Creates a new Content instance with validation.
    /// </summary>
    /// <param name="value">Содержимое / Content.</param>
    /// <returns>Результат с Content или ошибкой / Result with Content or error.</returns>
    public static Result<Content, Error> Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid("content");
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid("content");
        }

        return new Content(trimmed);
    }
}
