namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — подробное описание в формате markdown.
///     Инвариант: не пустое, не длиннее <see cref="MAX_LENGTH"/> символов.
/// </summary>
public sealed record DetailedDescription
{
    public const int MAX_LENGTH = 50_000;

    private DetailedDescription(string value) => Value = value;

    public string Value { get; }

    public static Result<DetailedDescription, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid("подробное описание");
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid("подробное описание");
        }

        return new DetailedDescription(trimmed);
    }
}
