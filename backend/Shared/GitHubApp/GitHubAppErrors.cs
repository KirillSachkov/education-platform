using SharedKernel;

namespace Shared.GitHubApp;

/// <summary>
///     Domain errors для GitHub App-интеграции. Stable error codes — стабильный
///     контракт с фронтом / consumer'ами событий.
///
///     Скопировано из <c>AccessService.Domain.GitHubAppErrors</c> (canonical) при
///     extract'е в Shared (#296). ARS до сих пор использует свой <c>VcsErrors</c>
///     namespace — миграция на эти коды в Phase 4 если/когда сервис переедет
///     на Shared-сервис.
/// </summary>
public static class GitHubAppErrors
{
    public static Error InstallationNotFound() =>
        Error.NotFound("github_app.installation.not.found", "GitHub App не установлен у автора");

    public static Error InstallStateInvalid() =>
        Error.Validation("github_app.install.state.invalid", "Недействительный state token (истёк или подделан)");

    public static Error InstallationSuspended() =>
        Error.Validation("github_app.installation.suspended", "GitHub App suspended в org автора");

    public static Error InvitationNotFound() =>
        Error.NotFound("github_app.invitation.not.found", "Приглашение не найдено");

    public static Error UserGithubLoginMissing() =>
        Error.Validation("github_app.user.login.missing", "Сначала привяжи GitHub-аккаунт");

    public static Error PlanGithubOrgMissing() =>
        Error.Validation("github_app.plan.org.missing", "У плана не задан GitHub org");

    public static Error WebhookSignatureInvalid() =>
        Error.Authorization("github_app.webhook.signature.invalid", "Webhook подпись не верна");

    public static Error AppApiCallFailed(string reason) =>
        Error.Failure("github_app.api.failed", $"GitHub API ошибка: {reason}");

    public static Error InvitationFailed(string reason) =>
        Error.Validation("github_app.invitation.failed", $"Не удалось отправить приглашение: {reason}");
}
