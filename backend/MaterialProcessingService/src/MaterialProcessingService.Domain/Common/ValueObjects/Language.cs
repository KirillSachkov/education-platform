using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Common.ValueObjects;

public sealed record Language
{
    private static readonly Regex _languageRegex = new("^[a-z]{2,8}(-[a-z0-9]{2,8})?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    public const int MAX_LENGTH = 16;

    private Language(string value) => Value = value;

    public string Value { get; }

    public static Result<Language, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GeneralErrors.ValueIsRequired(nameof(Language));

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MAX_LENGTH || !_languageRegex.IsMatch(normalized))
            return GeneralErrors.ValueIsInvalid(nameof(Language));

        return new Language(normalized);
    }
}
