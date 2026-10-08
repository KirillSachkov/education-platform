using System.Text.RegularExpressions;

namespace ProgressService.Domain.IssueSubmissions;

/// <summary>
/// Значение, содержащее данные, которые пользователь отправляет вместе с решением задачи.
/// Инкапсулирует базовую валидацию содержимого submission.
/// </summary>
public sealed partial record IssueSubmissionPayload
{
    public const int MaxLength = 10000;

    private IssueSubmissionPayload(string value)
    {
        Value = value;
    }

    public string Value { get; }

    private static readonly HashSet<string> _allowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "sachkov-learn.net",
    };

    // github.com submission должен вести на КОНКРЕТНЫЙ pull request, а не на страницу
    // «создать PR» (/pull/new/...), голый репозиторий или ветку (/tree/...). Иначе ARS
    // (его regex требует /pull/{N}) не создаёт AiReview → в панели проверки автор видит
    // пустой AI-статус, хотя ничего не проверяется (#718).
    // ДЕРЖАТЬ В СИНХРОНЕ с зеркальными регулярками:
    //   - ARS  GITHUB_PR_URL_REGEX — AssignmentReviewService.Core/.../IssueSubmissionAwaitingReviewHandler.cs
    //   - front isGitHubPullRequestUrl — frontend/src/entities/review-submission/lib.ts
    [GeneratedRegex(
        @"^https://github\.com/[^/]+/[^/]+/pull/\d+(?:[/?#].*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPullRequestUrlRegex();

    public static Result<IssueSubmissionPayload, Error> Create(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return GeneralErrors.ValueIsInvalid(nameof(payload));
        }

        string normalized = payload.Trim();
        if (normalized.Length > MaxLength)
        {
            return ProgressErrors.ValueLengthExceeded(nameof(payload), MaxLength);
        }

        bool isAbsoluteHttpUrl = Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
        if (!isAbsoluteHttpUrl)
        {
            return ProgressErrors.SubmissionUrlMustBeAbsoluteHttpUrl(nameof(payload));
        }

        if (!_allowedDomains.Any(domain =>
                uri!.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
        {
            return ProgressErrors.SubmissionUrlDomainNotAllowed(nameof(payload));
        }

        // Для github.com (и его поддоменов) требуем форму pull request. sachkov-learn.net
        // остаётся свободной формой (у него нет PR-контракта) — не форсим (#718).
        bool isGitHubHost =
            uri!.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
        if (isGitHubHost && !GitHubPullRequestUrlRegex().IsMatch(normalized))
        {
            return ProgressErrors.SubmissionUrlNotPullRequest();
        }

        return new IssueSubmissionPayload(normalized);
    }

    public static Result<IssueSubmissionPayload, Error> CreateSelfCheck(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return GeneralErrors.ValueIsInvalid(nameof(payload));
        }

        string normalized = payload.Trim();
        if (normalized.Length > MaxLength)
        {
            return ProgressErrors.ValueLengthExceeded(nameof(payload), MaxLength);
        }

        return new IssueSubmissionPayload(normalized);
    }

    public static Result<IssueSubmissionPayload, Error> Restore(string? payload) =>
        CreateSelfCheck(payload);
}
