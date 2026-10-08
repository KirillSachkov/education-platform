namespace AuthService.Domain.ValueObjects;

/// <summary>Ссылка на GitHub-профиль студента.</summary>
public record GitHubUrl
{
    public const int MAX_LENGTH = 255;

    private GitHubUrl(string value) => Value = value;

    public string Value { get; }

    public static Result<GitHubUrl, Error> Create(string value)
    {
        string normalized = value.Trim();

        if (normalized.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid($"github url (max {MAX_LENGTH} символов)");

        return new GitHubUrl(normalized);
    }

    public static Result<GitHubUrl?, Error> CreateOptional(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (GitHubUrl?)null;

        Result<GitHubUrl, Error> result = Create(raw);
        if (result.IsFailure)
            return result.Error;

        return result.Value;
    }
}
