namespace EducationContentService.Domain.Projects.ValueObjects;

/// <summary>
///     Value Object — URL-адрес внешнего ресурса.
///     Инвариант: валидный абсолютный URI, не длиннее <see cref="MAX_LENGTH"/> символов.
/// </summary>
public sealed record Url
{
    public const int MAX_LENGTH = 2048;

    private Url(string value) => Value = value;

    public string Value { get; }

    public static Result<Url, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsInvalid(nameof(value));
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid(nameof(value));
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralErrors.ValueIsInvalid(nameof(value));
        }

        return new Url(trimmed);
    }
}
