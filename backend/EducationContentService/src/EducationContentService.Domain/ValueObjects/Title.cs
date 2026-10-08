using System.Text.RegularExpressions;

namespace EducationContentService.Domain.ValueObjects;

/// <summary>
///     Value Object — название элемента образовательного контента.
///     Инвариант: не пустое, не длиннее <see cref="MAX_LENGTH"/> символов, нормализованные пробелы.
/// </summary>
public sealed record Title
{
    public const int MAX_LENGTH = 200;

    private Title(string value) => Value = value;

    public string Value { get; }

    public static Result<Title, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid("название");
        }

        string normalized = Regex.Replace(value.Trim(), @"\s+", " ");

        if (normalized.Length > MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid("название");
        }

        return new Title(normalized);
    }
}