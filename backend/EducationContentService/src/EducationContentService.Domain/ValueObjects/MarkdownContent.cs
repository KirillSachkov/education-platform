namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — контент в формате Markdown.
///     Инвариант: не пустой, не длиннее <see cref="MAX_LENGTH"/> символов.
/// </summary>
public sealed record MarkdownContent
{
    public const int MAX_LENGTH = 1_000_000;

    private MarkdownContent(string value) => Value = value;

    public string Value { get; }

    public static Result<MarkdownContent, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsRequired("описание");
        }

        // Preserve markdown formatting - only trim leading/trailing whitespace
        string trimmed = value.Trim();

        if (trimmed.Length > MAX_LENGTH)
        {
            return GeneralErrors.LengthIsInvalid("описание", max: MAX_LENGTH);
        }

        return new MarkdownContent(trimmed);
    }
}
