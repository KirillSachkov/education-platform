namespace ContentAccess;

/// <summary>
/// Результат проверки доступа с декодированной причиной блокировки для UI-подсказок.
/// Используется, когда фид/поисковая выдача показывает ВСЁ опубликованное, а
/// недоступный контент рисует с замком + CTA («Записаться», «Войти» и т. п.).
/// </summary>
public readonly record struct AccessLockResult(bool IsAccessible, string? LockReason);

/// <summary>
/// Декодирует набор required_access_tags + user grants в <see cref="AccessLockResult"/>.
/// Единый источник правды для MaterialFeedEnricher.
/// </summary>
public static class LockReasonResolver
{
    /// <summary>
    /// Определяет, доступен ли ресурс, и если нет — по какой причине.
    /// </summary>
    /// <param name="requiredAccessTags">
    ///   Теги доступа ресурса (из
    ///   <c>ContentAccessTagBuilder</c> в EducationContentService).
    ///   Пустой список = PUBLIC (доступен всем).
    /// </param>
    /// <param name="userGrants">
    ///   Набор grant-тегов пользователя из Redis (пустой для анонима).
    /// </param>
    /// <param name="isAuthenticated">
    ///   Авторизован ли пользователь — нужен чтобы отличить <c>anonymous</c> от <c>not_enrolled</c>.
    /// </param>
    public static AccessLockResult Resolve(
        IReadOnlyCollection<string> requiredAccessTags,
        EntitlementGrantSet userGrants,
        bool isAuthenticated)
    {
        // Пустой список required-тегов — safety-net: такой ресурс открыт всем.
        // В production ContentAccessTagBuilder для PUBLIC возвращает [GrantTags.PUBLIC],
        // не пустой массив — такие ресурсы разрешаются через match на PUBLIC в userGrants
        // (который caller добавляет всегда, см. EducationSearchAccessFilterBuilder).
        if (requiredAccessTags.Count == 0)
        {
            return new AccessLockResult(IsAccessible: true, LockReason: null);
        }

        bool hasMatchingGrant = false;
        foreach (string tag in requiredAccessTags)
        {
            if (userGrants.Contains(tag))
            {
                hasMatchingGrant = true;
                break;
            }
        }

        if (hasMatchingGrant)
        {
            return new AccessLockResult(IsAccessible: true, LockReason: null);
        }

        if (!isAuthenticated)
        {
            return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.ANONYMOUS);
        }

        // Phase E: семантика locked-причин завязана на plan-tag типах в resource set:
        //   - есть `plan:all`, `plan:course:*` или legacy `plan:lifetime:author_*` → нужен платный план
        //     → PLAN_REQUIRED (CTA «Купить план»)
        // Legacy ветки `course:` / `course::trial` остались для совместимости с
        // resource sets, которые ещё не пересобрали после cutover. Они приоритетно
        // ниже plan-проверок — после Phase E большая часть ресурсов содержит
        // только plan-tags.
        bool hasPlanCourseOrLifetimeRequirement = false;
        bool hasLegacyCourseTrialRequirement = false;
        bool hasLegacyCourseStandardRequirement = false;
        bool hasTrialForSomeLegacyCourse = false;

        foreach (string tag in requiredAccessTags)
        {
            if (GrantTags.IsPlanAllGrant(tag) || GrantTags.IsPlanCourseGrant(tag) || GrantTags.IsPlanLifetimeGrant(tag))
            {
                hasPlanCourseOrLifetimeRequirement = true;
            }
            else if (GrantTags.IsCourseGrant(tag))
            {
                hasLegacyCourseStandardRequirement = true;
                if (userGrants.Contains(tag + GrantTags.TRIAL_SUFFIX))
                {
                    hasTrialForSomeLegacyCourse = true;
                }
            }
            else if (GrantTags.IsCourseTrialGrant(tag))
            {
                hasLegacyCourseTrialRequirement = true;
            }
        }

        // Plan-only → платный план.
        if (hasPlanCourseOrLifetimeRequirement)
        {
            return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.PLAN_REQUIRED);
        }

        // Legacy: пользователь имеет trial на нужном курсе, но материал требует STANDARD.
        if (hasLegacyCourseStandardRequirement && hasTrialForSomeLegacyCourse)
        {
            return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.STANDARD_REQUIRED);
        }

        if (hasLegacyCourseTrialRequirement)
        {
            return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.TRIAL_REQUIRED);
        }

        return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.NOT_ENROLLED);
    }
}

/// <summary>
/// Строковые коды причин блокировки. Стабильный API между бэком и фронтом —
/// не менять существующие значения.
/// </summary>
public static class LockReasons
{
    /// <summary>Нужно войти в аккаунт.</summary>
    public const string ANONYMOUS = "anonymous";

    /// <summary>
    /// Legacy course-trial требование — у ресурса есть тег <c>course:X:trial</c>.
    /// CTA «Получить пробный доступ». Сохранено для совместимости с resource sets,
    /// которые ещё не пересобрали после plan-cutover.
    /// </summary>
    public const string TRIAL_REQUIRED = "trial_required";

    /// <summary>Уже есть trial, но требуется полная STANDARD-запись.</summary>
    public const string STANDARD_REQUIRED = "standard_required";

    /// <summary>Требуется запись на курс, trial'а тоже нет.</summary>
    public const string NOT_ENROLLED = "not_enrolled";

    /// <summary>
    /// Требуется покупка платного плана доступа (LIFETIME_ALL / COURSES).
    /// CTA «Купить план» (роутинг на /pricing).
    /// </summary>
    public const string PLAN_REQUIRED = "plan_required";
}
