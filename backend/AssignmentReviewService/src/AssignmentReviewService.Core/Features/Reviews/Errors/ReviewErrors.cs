using SharedKernel;

namespace AssignmentReviewService.Core.Features.Reviews.Errors;

/// <summary>
///     Доменные ошибки run-iteration pipeline'а. Codes стабильны — попадают в
///     БД (<c>ai_review_iterations.failure_reason</c>) и в UI lock-copy.
/// </summary>
public static class ReviewErrors
{
    public static Error ReviewNotFound(Guid reviewId) =>
        Error.NotFound("review.not_found", $"AI-проверка {reviewId} не найдена.");

    public static Error AccessDenied() =>
        Error.Failure("review.access_denied", "Недостаточно прав для запуска этой AI-проверки.");

    public static Error ReviewAlreadyRunning(Guid reviewId) =>
        Error.Validation("review.already_running",
            $"Iteration уже выполняется для AI-проверки {reviewId}. Дождитесь завершения.");

    public static Error ReviewSuperseded(Guid reviewId) =>
        Error.Conflict("review.superseded",
            $"AI-проверка {reviewId} уже была остановлена или перезапущена.");

    // Conflict (409), не Failure (500): отключённый админом тумблер — это валидное
    // состояние, а не сбой сервера. Иначе deliberate-гейт засорял бы 5xx-алерты прода.
    public static Error ReviewDisabled() =>
        Error.Conflict("review.disabled",
            "AI-проверка пул-реквестов отключена администратором платформы.");

    public static Error RateLimitExceeded(string scope, int limit, int observed) =>
        Error.Failure("review.rate_limit.exceeded",
            $"Превышен лимит AI-проверок: {scope} ({observed}/{limit}). Попробуй позже.");

    public static Error NoInstallation(string repoFullName) =>
        Error.Failure("review.no_installation",
            $"GitHub App не установлен для {repoFullName}. Подключи интеграцию в /settings/integrations.");

    public static Error RepoNotInInstallation(string repoFullName) =>
        Error.Failure("review.repo.not_in_installation",
            $"{repoFullName} не входит в whitelist GitHub App. Открой доступ в настройках App.");

    public static Error DiffTooLarge(int additions, int maxAdditions, bool manualLimit = false) =>
        Error.Validation("review.diff.too_large",
            manualLimit
                ? $"PR слишком большой для безопасной AI-проверки ({additions}/{maxAdditions} добавленных строк). " +
                  "Разбейте изменения на несколько PR."
                : $"PR большой ({additions} добавленных строк) — авто-проверка пропущена. " +
                  "Автор может запустить AI-проверку вручную: она разобьёт PR на части и проверит целиком.");

    public static Error GitHubUnavailable(string detail) =>
        Error.Failure("review.github.unavailable", $"GitHub недоступен: {detail}. Попробуй позже.");

    // 4xx от GitHub на POST review — наш payload отвергнут (inline-комментарий вне diff'а,
    // конфликт со state PR и т.п.). Ретрай того же payload'а бесполезен — нужна новая
    // итерация LLM с другим выводом. Отдельный код, чтобы UI не предлагал «попробуй позже».
    public static Error GitHubInvalidRequest(string detail) =>
        Error.Failure("review.github.invalid_request",
            $"GitHub отверг запрос ({detail}). AI выдал некорректный отзыв — нажми «Перепроверить» " +
            "для новой попытки.");

    public static Error LlmUnavailable(string detail) =>
        Error.Failure("review.llm.unavailable", $"AI-провайдер недоступен: {detail}. Попробуй позже.");

    public static Error LlmInvalidOutput(string detail) =>
        Error.Failure("review.llm.invalid_output",
            $"AI вернул некорректный ответ ({detail}). Попробуй ещё раз.");

    public static Error DraftNotSupported(string repoFullName, int pullNumber) =>
        Error.Validation("review.pr.draft_not_supported",
            $"PR {repoFullName}#{pullNumber} находится в Draft. " +
            "Переведи PR в Ready for review и запусти проверку снова.");

    // Студенческая доработка (#725, гейт переписан в #976) доступна, когда по сдаче уже
    // прошла хотя бы одна ЗАВЕРШЁННАЯ AI-итерация. Отказ остаётся только для сдачи, которую
    // AI ещё ни разу не проверила (свежий QUEUED-review или все итерации упали): перепроверять
    // нечего. Conflict (409): это валидное состояние, а не сбой сервера.
    public static Error RerunNotAvailable() =>
        Error.Conflict("review.rerun.not_available",
            "Повторная проверка доступна после того, как AI хотя бы раз проверила эту сдачу.");
}
