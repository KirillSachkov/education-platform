namespace AssignmentReviewService.Core.Vcs;

public static class VcsErrors
{
    public static Error InstallationTokenFailed(string detail) =>
        Error.Failure(
            "vcs.installation_token.failed",
            $"Не удалось получить installation token GitHub App: {detail}");

    public static Error PullRequestNotFound(string repoFullName, int pullNumber) =>
        Error.NotFound(
            "vcs.pull_request.not_found",
            $"PR {repoFullName}#{pullNumber} не найден или нет доступа.");

    public static Error RepoNotInInstallation(string repoFullName) =>
        Error.Failure(
            "vcs.repo.not_in_installation",
            $"Репозиторий {repoFullName} не входит в whitelist установки GitHub App.");

    public static Error UnauthorizedFromVcs() =>
        Error.Failure(
            "vcs.unauthorized",
            "GitHub отверг запрос (401 / 403). Проверь App private key и installation status.");

    public static Error VcsUnavailable(string detail) =>
        Error.Failure("vcs.unavailable", $"VCS provider временно недоступен: {detail}");

    public static Error VcsInvalidRequest(string detail) =>
        Error.Failure("vcs.invalid_request", $"VCS provider отверг запрос: {detail}");

    public static Error MalformedResponse(string detail) =>
        Error.Failure("vcs.response.malformed", $"VCS provider вернул некорректный ответ: {detail}");

    public static Error InstallationIdInvalid(string raw) =>
        Error.Validation(
            "vcs.installation_id.invalid",
            $"InstallationId не валидное число: '{raw}'. GitHub installations нумеруются числами.");

    public static Error ResourceNotFound(string detail) =>
        Error.NotFound("vcs.resource.not_found", $"VCS resource не найден: {detail}");

    /// <summary>
    ///     1.6 hardening (#264): GitHub primary/secondary rate-limit (HTTP 429 или
    ///     403 с <c>X-RateLimit-Remaining: 0</c>). <paramref name="retryAfterSeconds"/>
    ///     извлекается из <c>Retry-After</c> header'а (либо <c>X-RateLimit-Reset</c>
    ///     если 429 не пришёл с Retry-After). Используется call-site'ами для повтора /
    ///     surfacing'а в UI (например, run-iteration → 429 с Retry-After header).
    /// </summary>
    public static Error RateLimited(int? retryAfterSeconds) =>
        Error.Failure(
            "vcs.rate_limited",
            retryAfterSeconds.HasValue
                ? $"GitHub API rate-limit. Повторите через {retryAfterSeconds.Value} с."
                : "GitHub API rate-limit. Повторите позже.");
}
