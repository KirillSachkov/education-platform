namespace AccessService.Contracts.Onboarding;

/// <summary>
///     Включить / выключить онбординг для плана.
/// </summary>
public sealed record SetOnboardingEnabledRequest(bool IsEnabled);

/// <summary>
///     Создать MARKDOWN-шаг онбординга. Шаг добавляется в конец списка.
/// </summary>
public sealed record AddMarkdownStepRequest(
    string Title,
    string Body,
    bool IsSkippable);

/// <summary>
///     Обновить MARKDOWN-шаг (title, body, isSkippable).
/// </summary>
public sealed record UpdateMarkdownStepRequest(
    string Title,
    string Body,
    bool IsSkippable);

/// <summary>
///     Переупорядочить шаг — указать <see cref="BeforeStepId"/> и/или
///     <see cref="AfterStepId"/> для вычисления новой fractional-позиции.
/// </summary>
public sealed record ReorderStepRequest(
    Guid? BeforeStepId,
    Guid? AfterStepId);

/// <summary>
///     Включить / выключить возможность пропуска шага. Применяется ко всем типам
///     шагов, включая авто (TG/GH/NOTIF).
/// </summary>
public sealed record SetStepIsSkippableRequest(bool IsSkippable);

/// <summary>
///     GitHub login для live-проверки membership support/admin-сценарием.
/// </summary>
public sealed record RecheckGithubMembershipRequest(string? GithubLogin);

/// <summary>
///     Включить / выключить GITHUB_REVIEW_APP шаг онбординга (issue #307). Автор
///     решает, должен ли студент при онбординге подключить AssignmentReviewService
///     GitHub App для AI-проверки PR'ов. <c>IsEnabled=true</c> — добавляет шаг
///     если его нет; <c>false</c> — убирает (idempotent).
/// </summary>
public sealed record ToggleGithubReviewAppStepRequest(bool IsEnabled);
