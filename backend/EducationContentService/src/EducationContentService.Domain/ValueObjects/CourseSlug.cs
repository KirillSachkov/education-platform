using System.Text.RegularExpressions;

namespace EducationContentService.Domain.ValueObjects;

/// <summary>URL-безопасный слаг курса.</summary>
public partial record CourseSlug
{
    public const int MIN_LENGTH = 2;
    public const int MAX_LENGTH = 100;

    private CourseSlug(string value) => Value = value;

    public string Value { get; }

    public static Result<CourseSlug, Error> Create(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length < MIN_LENGTH || normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid(
                $"slug (от {MIN_LENGTH} до {MAX_LENGTH} символов)");

        if (!SlugPattern().IsMatch(normalized))
            return GeneralErrors.ValueIsInvalid(
                "slug (только строчные латинские буквы, цифры и дефисы, не начинается и не заканчивается дефисом)");

        return new CourseSlug(normalized);
    }

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();
}
