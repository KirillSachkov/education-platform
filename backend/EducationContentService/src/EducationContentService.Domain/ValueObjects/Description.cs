using System.Text.RegularExpressions;

namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — описание элемента образовательного контента.
///     Инвариант: не пустое, не длиннее <see cref="MAX_LENGTH"/> символов, нормализованные пробелы.
/// </summary>
public sealed record Description
{
    public const int MAX_LENGTH = 2000;

    private Description(string value) => Value = value;

    public string Value { get; }

    public static Result<Description, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid("описание");
        }

        string normalized = Regex.Replace(value.Trim(), @"\s+", " ");

        if (normalized.Length > MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid("описание");
        }

        return new Description(normalized);
    }
}