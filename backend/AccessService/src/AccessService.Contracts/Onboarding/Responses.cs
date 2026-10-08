namespace AccessService.Contracts.Onboarding;

/// <summary>
///     Ответ admin-эндпоинта <c>GET /access/plans/{id}/onboarding-flow</c>.
/// </summary>
public sealed record OnboardingFlowResponse(
    Guid PlanId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnboardingStepDto> Steps);

/// <summary>
///     Шаг онбординга. Для AUTO-шагов (TELEGRAM/GITHUB/NOTIFICATIONS) Title/Body = null.
/// </summary>
public sealed record OnboardingStepDto(
    Guid Id,
    string StepType,
    bool IsSkippable,
    string SortOrder,
    string? Title,
    string? Body);

/// <summary>
///     Текущий онбординг для пользователя — шаги + state. Возвращается из
///     <c>GET /access/onboarding/current</c>. 204 No Content если нет pending.
/// </summary>
public sealed record CurrentOnboardingResponse(
    Guid PlanId,
    string PlanDisplayName,
    Guid PlanAuthorId,
    /// <summary>
    ///     GitHub-org slug плана, если автор настроил автоматический инвайт.
    ///     Null если нет интеграции — frontend по нему понимает что GITHUB-step
    ///     надо показать как «свяжитесь с автором» либо matches profile.githubOrgs.
    /// </summary>
    string? PlanGitHubOrgSlug,
    IReadOnlyList<OnboardingStepDto> Steps,
    UserOnboardingStateDto State);

/// <summary>
///     Состояние прохождения онбординга.
/// </summary>
public sealed record UserOnboardingStateDto(
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? CurrentStepId,
    IReadOnlyList<Guid> SkippedStepIds,
    IReadOnlyList<Guid> CompletedStepIds);

/// <summary>
///     Результат массового сброса онбординга по плану
///     (<c>POST /access/plans/{planId}/onboarding-flow/reset-all/</c>).
///     <see cref="ResetCount"/> = число пользовательских онбордингов, сброшенных на
///     первый шаг. <c>0</c> если у плана нет ни одного onboarding-state.
/// </summary>
public sealed record ResetAllOnboardingsResponse(int ResetCount);

/// <summary>
///     Результат ручного рекчека TELEGRAM-шага онбординга
///     (<c>POST /access/onboarding/{planId}/steps/telegram/recheck/</c>).
///     <see cref="Completed"/> = TELEGRAM-шаг помечен пройденным в этом запросе или
///     уже был пройден. <see cref="Status"/> зеркалит
///     <c>PlanMembershipDto.Status</c> (<c>member | not_member | unknown</c>) либо
///     <c>unknown</c> при недоступности TelegramBotService (soft-degrade).
/// </summary>
public sealed record RecheckTelegramMembershipResponse(bool Completed, string Status);

/// <summary>
///     Результат support/admin live-проверки GITHUB-шага онбординга.
///     <see cref="Status"/>: <c>member | not_member | unknown</c>.
/// </summary>
public sealed record RecheckGithubMembershipResponse(bool Completed, string Status);
