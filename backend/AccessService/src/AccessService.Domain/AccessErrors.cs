namespace AccessService.Domain;

/// <summary>
/// Domain error factories. Codes follow "{entity}.{condition}" convention.
/// Messages are Russian by convention (user-facing via API Envelope.message).
/// </summary>
public static class AccessErrors
{
    public static Error RetiredOffer() =>
        Error.Validation("plan.offer.retired", "Этот тип плана больше недоступен");

    public static Error PlanSlugConflict(string slug) =>
        Error.Conflict("plan.slug.conflict", $"План со slug '{slug}' уже существует на платформе");

    public static Error PlanNotFound() =>
        Error.NotFound("plan.not.found", "План не найден");

    public static Error PlanArchived() =>
        Error.Validation("plan.archived", "План архивирован");

    public static Error PlanHasPaidOrders() =>
        Error.Conflict("plan.delete.has_paid_orders",
            "Нельзя удалить план: по нему были оплаты. Архивируйте план вместо удаления.");

    public static Error PlanHasOrders() =>
        Error.Conflict("plan.delete.has_orders",
            "Нельзя удалить план: по нему уже создан заказ. Архивируйте план вместо удаления.");

    public static Error PlanHasActiveGrants() =>
        Error.Conflict("plan.delete.has_active_grants",
            "Нельзя удалить план: есть активные доступы. Отзовите доступы или архивируйте план.");

    public static Error PlanCoursesEmpty() =>
        Error.Validation("plan.courses.empty", "Для плана типа COURSE нужно указать хотя бы один курс");

    public static Error LifetimeShouldNotHaveCourses() =>
        Error.Validation("plan.lifetime.with.courses", "Для плана LEARN_ALL/FULL_ALL не указываются конкретные курсы");

    public static Error CourseRequiredForCourseTier() =>
        Error.Validation("plan.course_id.required", "Для плана типа COURSE требуется указать CourseId");

    public static Error CourseForbiddenForTier() =>
        Error.Validation("plan.course_id.forbidden", "Для плана не типа COURSE нельзя указывать CourseId");

    public static Error LearnAllDeprecated() =>
        Error.Validation("plan.learn_all.deprecated", "Тип плана LEARN_ALL устарел; создание новых планов этого типа невозможно");

    public static Error SubscriptionRequiresRecurringTerm() =>
        Error.Validation("plan.subscription.requires_recurring_term",
            "Подписочный план должен иметь периодический срок действия с положительным интервалом в днях");

    public static Error OfferTypeInvalidForTier() =>
        Error.Validation("plan.offer_type.invalid_for_tier",
            "Этот формат оффера недоступен для выбранного типа плана");

    public static Error FreeDeprecated() =>
        Error.Validation("plan.free.deprecated",
            "Бесплатный план больше не создаётся вручную — бесплатный доступ доступен всем зарегистрированным пользователям по умолчанию");

    public static Error PlanTierDuplicate(PlanTier tier) =>
        Error.Conflict("plan.tier.duplicate",
            $"На платформе уже есть активный план типа {tier}. Архивируйте старый или используйте COURSE-план");

    public static Error PlanTrialDuplicate() =>
        Error.Conflict("plan.trial.duplicate",
            "На платформе уже есть активный пробный доступ. Архивируйте старый, прежде чем публиковать новый");

    public static Error PlanTrialReadOnly() =>
        Error.Conflict("plan.trial.read_only",
            "Пробный месячный доступ — системный план, его нельзя редактировать как обычный план");

    public static Error CoursePlanAlreadyActive() =>
        Error.Conflict("plan.course.duplicate_active",
            "На этот курс уже есть активный публичный план. Архивируйте или измените существующий, прежде чем создавать новый.");

    public static Error InviteNotFound() =>
        Error.NotFound("invite.not.found", "Инвайт-ссылка не найдена");

    public static Error InviteRevoked() =>
        Error.Validation("invite.revoked", "Инвайт-ссылка отозвана");

    public static Error InviteExpired() =>
        Error.Validation("invite.expired", "Срок действия инвайт-ссылки истёк");

    public static Error InviteUsageExhausted() =>
        Error.Validation("invite.usage.exhausted", "Лимит использований инвайт-ссылки исчерпан");

    public static Error GrantNotFound() =>
        Error.NotFound("grant.not.found", "Grant не найден");

    public static Error GrantAlreadyExists() =>
        Error.Conflict("grant.already.exists", "У пользователя уже есть активный grant на этот план");

    public static Error GrantNotActive() =>
        Error.Validation("grant.not.active", "Grant уже не активен");

    public static Error RecurringRefRequired() =>
        Error.Validation("grant.recurring.ref_required",
            "Для подписки нужны идентификаторы автосписания (RebillId и CustomerKey)");

    public static Error RecurringRefTooLong() =>
        Error.Validation("grant.recurring.ref_too_long",
            "Идентификатор автосписания слишком длинный");

    public static Error RecurringRefConflict() =>
        Error.Conflict("grant.recurring.ref_conflict",
            "У grant'а уже привязан другой RebillId — нельзя изменить");

    public static Error RecurringNotConfigured() =>
        Error.Validation("grant.recurring.not_configured",
            "Для grant'а не настроено автопродление");

    public static Error AutoRenewalCancelled() =>
        Error.Validation("grant.renewal.cancelled",
            "Автопродление grant'а отменено");

    public static Error RenewalGraceInvalid() =>
        Error.Validation("grant.renewal.grace_invalid",
            "Grace period не может заканчиваться раньше оплаченного периода");

    public static Error RenewalRetryAfterGrace() =>
        Error.Validation("grant.renewal.retry_after_grace",
            "Повторное списание нельзя планировать после grace period");

    public static Error RenewalPeriodEnded() =>
        Error.Validation("grant.renewal.period_ended",
            "Оплаченный период уже завершён — требуется новая покупка");

    public static Error RenewalLeadTimeInvalid() =>
        Error.Validation("grant.renewal.lead_time_invalid",
            "Lead time автосписания должен быть положительным");

    public static Error AccessDenied() =>
        Error.Authorization("access.denied", "Доступ запрещён");

    public static Error CourseAuthorMismatch() =>
        Error.Validation("course.author.mismatch", "Указанный автор не владеет этим курсом");

    public static Error FreePlanCannotHaveCourses() =>
        Error.Validation("plan.free.with.courses", "Бесплатный план не указывает курсы — он покрывает legacy FREE-контент");

    public static Error FreePlanAlreadyClaimed() =>
        Error.Conflict("plan.free.already.claimed", "Бесплатный план уже получен");

    public static Error PlanIsNotFree() =>
        Error.Validation("plan.not.free", "Запрошенный план не является бесплатным");

    public static Error PlanInactive() =>
        Error.Validation("plan.inactive", "План не активен");

    public static Error PlanGithubOrgInvalid() =>
        Error.Validation("plan.github.org.invalid", "Недопустимый GitHub-org slug (буквы, цифры, дефисы, до 39 символов)");

    public static Error PlanGithubOrgConflict(string slug) =>
        Error.Conflict("plan.github.org.conflict", $"GitHub-org '{slug}' уже привязан к другому активному плану");

    public static Error PromotionRequiresPrice() =>
        Error.Validation("plan.promotion.requires_price", "Скидку можно установить только для плана с ценой");

    public static Error PromotionPercentInvalid() =>
        Error.Validation("plan.promotion.percent_invalid", "Процент скидки должен быть от 1 до 99");

    public static Error PromotionWindowInvalid() =>
        Error.Validation("plan.promotion.window_invalid", "Дата окончания должна быть позже даты начала");

    public static Error PromotionWindowInPast() =>
        Error.Validation("plan.promotion.window_in_past", "Дата окончания акции должна быть в будущем");

    public static Error TrialAlreadyUsed() =>
        Error.Validation("order.trial.already_used", "Вы уже использовали пробный месяц этого плана");

    /// <summary>Legacy retired offers cannot be purchased through the platform endpoint.</summary>
    public static Error OrderTrainerScopeOnlyOnTrainerEndpoint() =>
        Error.Validation("order.plan.trainer_only",
            "Этот план больше недоступен для покупки");



    public static Error TrialTierInvalid() =>
        Error.Validation("plan.trial.tier_invalid",
            "Пробный период доступен только для плана полного доступа (FULL_ALL)");

    public static Error TrialDurationInvalid() =>
        Error.Validation("plan.trial.duration_invalid",
            "Длительность пробного периода должна быть положительным числом дней");

    public static Error TrialOverrideUntilInPast() =>
        Error.Validation("trial.override.until_in_past",
            "Дата окончания зачёта должна быть в будущем");
}

/// <summary>
///     Domain errors для онбординга планов.
/// </summary>
public static class OnboardingErrors
{
    public static Error FlowNotFound() =>
        Error.NotFound("onboarding.flow.not.found", "Онбординг для этого плана не настроен");

    public static Error StepNotFound() =>
        Error.NotFound("onboarding.step.not.found", "Шаг онбординга не найден");

    public static Error AutoStepNotEditable() =>
        Error.Validation("onboarding.step.auto.not.editable", "Авто-шаг (Telegram/GitHub/уведомления) не редактируется");

    public static Error AutoStepNotRemovable() =>
        Error.Validation("onboarding.step.auto.not.removable", "Авто-шаг управляется автоматически, его нельзя удалить вручную");

    public static Error MarkdownTitleRequired() =>
        Error.Validation("onboarding.step.title.required", "Заголовок шага обязателен");

    public static Error MarkdownTitleTooLong() =>
        Error.Validation("onboarding.step.title.too.long", "Заголовок не должен превышать 200 символов");

    public static Error MarkdownBodyRequired() =>
        Error.Validation("onboarding.step.body.required", "Текст шага обязателен");

    public static Error MarkdownBodyTooLong() =>
        Error.Validation("onboarding.step.body.too.long", "Текст шага слишком длинный (>50000 символов)");

    public static Error OnboardingNotFound() =>
        Error.NotFound("onboarding.not.found", "Онбординг не найден");

    public static Error FlowDisabled() =>
        Error.Validation("onboarding.flow.disabled", "Онбординг для этого плана выключен автором");

    public static Error OnboardingAlreadyCompleted() =>
        Error.Validation("onboarding.already.completed", "Онбординг уже пройден");

    public static Error StepNotSkippable() =>
        Error.Validation("onboarding.step.not.skippable", "Этот шаг нельзя пропустить");

    public static Error OnboardingHasPendingSteps() =>
        Error.Validation("onboarding.has.pending.steps", "Не все шаги пройдены или пропущены");

    public static Error TelegramMembershipRequired() =>
        Error.Validation("onboarding.telegram.membership.required",
            "Сначала вступите в Telegram-группу — членство не подтверждено. После вступления нажмите «Я вступил — проверить».");

    public static Error GithubMembershipRequired() =>
        Error.Validation("onboarding.github.membership.required",
            "Сначала примите приглашение в GitHub-организацию — членство пока не подтверждено.");

    public static Error GithubVerificationUnavailable() =>
        Error.Validation("onboarding.github.verification.unavailable",
            "Не удалось проверить членство в GitHub-организации: GitHub или интеграция автора сейчас недоступны. Попробуйте позже.");
}

/// <summary>
///     Domain errors для GitHub App-интеграции (auto-invite в org).
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

    public static Error UserGithubLoginInvalid() =>
        Error.Validation("github_app.user.login.invalid", "Некорректный GitHub login");

    public static Error PlanGithubOrgMissing() =>
        Error.Validation("github_app.plan.org.missing", "У плана не задан GitHub org");

    public static Error WebhookSignatureInvalid() =>
        Error.Authorization("github_app.webhook.signature.invalid", "Webhook подпись не верна");

    public static Error AppApiCallFailed(string reason) =>
        Error.Failure("github_app.api.failed", $"GitHub API ошибка: {reason}");

    public static Error InvitationFailed(string reason) =>
        Error.Validation("github_app.invitation.failed", $"Не удалось отправить приглашение: {reason}");
}