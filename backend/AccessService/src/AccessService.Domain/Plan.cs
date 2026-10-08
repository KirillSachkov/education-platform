using AccessService.Domain.Events;
using SharedKernel.DomainEvents;

namespace AccessService.Domain;

/// <summary>
/// Aggregate root: план доступа платформы с автором-владельцем. Tier — first-class
/// классификатор (<see cref="PlanTier"/>); published singleton-tier'ы (LEARN_ALL/FULL_ALL)
/// ограничены одним active+public экземпляром на платформу (partial unique index в БД).
/// </summary>
public sealed class Plan : AggregateRoot
{
    /// <summary>Лимит одного Telegram-сообщения — потолок для <see cref="TelegramWelcomeMessage"/>.</summary>
    public const int TELEGRAM_WELCOME_MAX_LENGTH = 4096;

    private readonly List<PlanCourse> _courses = [];

    private Plan() { } // EF

    private Plan(
        Guid id,
        Guid authorId,
        PlanTier tier,
        PlanOfferType offerType,
        PlanCapabilities capabilities,
        PlanSlug slug,
        PlanDisplayName displayName,
        bool includesFutureContent,
        DateTimeOffset createdAt)
    {
        Id = id;
        AuthorId = authorId;
        Tier = tier;
        OfferType = offerType;
        Scope = ResolveScope(offerType);
        Capabilities = capabilities;
        Slug = slug;
        DisplayName = displayName;
        IncludesFutureContent = includesFutureContent;
        IsActive = true;
        IsPublic = false;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>
    ///     Tier плана — source-of-truth. Определяет UI-категорию, scope доступа,
    ///     singleton-правило и default capabilities. См. <see cref="PlanTier"/>.
    /// </summary>
    public PlanTier Tier { get; private set; }

    public PlanSlug Slug { get; private set; } = null!;

    public PlanDisplayName DisplayName { get; private set; } = null!;

    /// <summary>
    ///     Маркетинг-формат оффера (бейдж / визуал в каталоге). Ортогонален
    ///     <see cref="Tier"/> (scope доступа). Forced <c>FULL_ACCESS</c> для
    ///     FULL_ALL/LEARN_ALL; для COURSE-tier — COURSE/INTENSIVE/MARATHON.
    ///     См. <see cref="PlanOfferType"/>.
    /// </summary>
    public PlanOfferType OfferType { get; private set; }

    /// <summary>
    ///     Каталожный scope плана (#674) — производная от <see cref="OfferType"/>:
    ///     <c>TRAINER_PRO → TRAINER</c>, всё остальное → <c>PLATFORM</c>. Source-of-truth: домен
    ///     пересчитывает его в factory и в <see cref="UpdateOfferType"/>, поэтому он всегда
    ///     консистентен с offer-type'ом и callers не могут выставить его рассогласованно.
    ///     Catalog/pricing read-пути фильтруют <c>Scope == PLATFORM</c>. См. <see cref="PlanScope"/>.
    /// </summary>
    public PlanScope Scope { get; private set; }

    public string ShortDescription { get; private set; } = string.Empty;

    public string LongDescription { get; private set; } = string.Empty;

    public Guid? CoverFileId { get; private set; }

    public IReadOnlyList<string> Features { get; private set; } = [];

    public long? PriceCents { get; private set; }

    public string Currency { get; private set; } = "RUB";

    /// <summary>
    /// Акция: процент скидки 1..99 от <see cref="PriceCents"/>. <c>null</c> = акции нет.
    /// Активна только в окне [<see cref="DiscountStartsAt"/>; <see cref="DiscountEndsAt"/>);
    /// эффективная цена считается в read-time (<see cref="EffectivePriceCents"/>) — фонового
    /// джоба нет, после <see cref="DiscountEndsAt"/> цена сама возвращается к обычной.
    /// Все три поля заполнены вместе либо все три <c>null</c> (см. <see cref="SetPromotion"/>).
    /// </summary>
    public int? DiscountPercent { get; private set; }

    public DateTimeOffset? DiscountStartsAt { get; private set; }

    public DateTimeOffset? DiscountEndsAt { get; private set; }

    /// <summary>
    /// Привязки курсов к плану (bundle, #404). Для <c>Tier == COURSE</c> — один или несколько
    /// курсов; для остальных тиров пусто. Меняется через <see cref="SetCourses"/>.
    /// </summary>
    public IReadOnlyList<PlanCourse> Courses => _courses;

    /// <summary>
    /// Удобный проекшн course ID'ов плана (из <see cref="Courses"/>). Метод (не property),
    /// потому что копирует коллекцию (S2365).
    /// </summary>
    public IReadOnlyList<Guid> GetCourseIds() => _courses.Select(c => c.CourseId).ToList();

    /// <summary>
    /// Первый course ID плана (legacy/fallback для integration events) или <c>null</c>, если
    /// курсов нет. Скаляр — не нарушает S2365/CA1826.
    /// </summary>
    public Guid? FirstCourseId => _courses.Count > 0 ? _courses[0].CourseId : null;

    public bool IncludesFutureContent { get; private set; }

    /// <summary>
    /// Длительность пробного периода в днях (#580). Если задано (&gt; 0) — план «пробный»:
    /// покупка выдаёт TTL-грант на это число дней вместо бессрочного. EF-маппится на
    /// колонку <c>trial_duration_days</c>. Допустимо только для FULL_ALL-тира.
    /// </summary>
    public int? TrialDurationDays { get; private set; }

    /// <summary>
    /// План «пробный» (time-bounded) — покупка выдаёт грант с TTL = <see cref="TrialDurationDays"/>
    /// дней. Исключён из singleton-tier уникальности (сосуществует с бессрочным FULL_ALL платформы).
    /// </summary>
    public bool IsTrial => TrialDurationDays is > 0;

    public PlanCapabilities Capabilities { get; private set; } = PlanCapabilities.FULL;

    public PlanTerm Term { get; private set; } = PlanTerm.Lifetime;

    public bool IsPublic { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Бейдж «ХИТ» / выделенный визуал на pricing-странице. По умолчанию ровно один
    /// план должен иметь <c>true</c> — flagship оффер платформы.
    /// </summary>
    public bool IsHighlighted { get; private set; }

    public int DisplayOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>
    /// GitHub organization slug — при логине через GitHub юзер, состоящий в этом org,
    /// автоматически получает PlanGrant. Один org может быть привязан только к одному
    /// активному плану (partial-unique index). Нормализован к lowercase.
    /// </summary>
    public string? GitHubOrg { get; private set; }

    /// <summary>
    /// Приветственное сообщение, которое бот постит в привязанную к плану Telegram-группу
    /// при входе нового участника. <c>null</c> = приветствие не настроено (бот ничего не постит).
    /// Author-edited markdown, max 4096 символов (лимит одного Telegram-сообщения).
    /// </summary>
    public string? TelegramWelcomeMessage { get; private set; }

    /// <summary>
    ///     Создаёт план по tier. Применяет tier-специфичные инварианты:
    ///     FULL_ALL → forced FULL caps, no CourseId, covers future;
    ///     COURSE → non-null CourseId required +
    ///     author-chosen capabilities (default = VIEW_MATERIALS+SUBMIT_ISSUES);
    ///     FREE / LEARN_ALL → deprecated (FREE collapsed to system-default registered
    ///     access #358; LEARN_ALL deprecated earlier);
    ///     SUBSCRIPTION → recurring auto-renew (#614): не привязан к курсам,
    ///     требует <paramref name="term"/> с <see cref="PlanTermKind.RECURRING"/> и
    ///     положительным интервалом; capabilities задаёт автор (как у COURSE).
    /// </summary>
    public static Result<Plan, Error> Create(
        Guid authorId,
        PlanTier tier,
        PlanSlug slug,
        PlanDisplayName displayName,
        IReadOnlyList<Guid> courseIds,
        IReadOnlyList<string>? requestedCapabilities,
        PlanOfferType? offerType = null,
        int? trialDurationDays = null,
        PlanTerm? term = null)
    {
        PlanTerm resolvedTerm = term ?? PlanTerm.Lifetime;
        // Dedup once up-front — used by both the COURSE-tier invariant and materialization.
        IReadOnlyList<Guid> distinctCourseIds = (courseIds ?? []).Distinct().ToList();

        PlanCapabilities capabilities;
        bool includesFuture;

        switch (tier)
        {
            case PlanTier.FREE:
                return AccessErrors.FreeDeprecated();

            case PlanTier.LEARN_ALL:
                return AccessErrors.LearnAllDeprecated();

            case PlanTier.FULL_ALL:
                if (distinctCourseIds.Count > 0)
                    return AccessErrors.CourseForbiddenForTier();
                capabilities = PlanCapabilities.FULL;
                includesFuture = true;
                break;

            case PlanTier.COURSE:
                if (distinctCourseIds.Count == 0)
                    return AccessErrors.CourseRequiredForCourseTier();
                capabilities = requestedCapabilities is { Count: > 0 }
                    ? PlanCapabilitiesMapper.FromStrings(requestedCapabilities)
                    : PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES;
                includesFuture = false;
                break;

            case PlanTier.SUBSCRIPTION:
                if (distinctCourseIds.Count > 0)
                    return AccessErrors.CourseForbiddenForTier();
                // Подписка (#614) обязана быть периодической: иначе нечего автопродлевать.
                if (resolvedTerm.Kind != PlanTermKind.RECURRING || resolvedTerm.RecurringIntervalDays is not > 0)
                    return AccessErrors.SubscriptionRequiresRecurringTerm();
                capabilities = requestedCapabilities is { Count: > 0 }
                    ? PlanCapabilitiesMapper.FromStrings(requestedCapabilities)
                    : PlanCapabilities.TRAINER_PRO;
                includesFuture = false;
                break;

            default:
                return Error.Validation("plan.tier.invalid", "Неизвестный тип плана");
        }

        // Пробный период (#580): допустим только для FULL_ALL (полный доступ на N дней).
        // Значение должно быть положительным. Для прочих тиров — ошибка.
        if (trialDurationDays is not null)
        {
            if (tier != PlanTier.FULL_ALL)
            {
                return AccessErrors.TrialTierInvalid();
            }

            if (trialDurationDays <= 0)
            {
                return AccessErrors.TrialDurationInvalid();
            }
        }

        // OfferType ортогонален tier'у, но валидируется по нему: FULL/LEARN-ALL forced
        // FULL_ACCESS; COURSE — COURSE/INTENSIVE/MARATHON (null → COURSE, FULL_ACCESS → ошибка).
        Result<PlanOfferType, Error> resolvedOffer = ResolveOfferType(tier, offerType);
        if (resolvedOffer.IsFailure)
        {
            return resolvedOffer.Error;
        }

        Plan plan = new(
            Guid.CreateVersion7(),
            authorId,
            tier,
            resolvedOffer.Value,
            capabilities,
            slug,
            displayName,
            includesFuture,
            DateTimeOffset.UtcNow);

        plan.TrialDurationDays = trialDurationDays;
        plan.Term = resolvedTerm;

        foreach (Guid courseId in distinctCourseIds)
        {
            plan._courses.Add(PlanCourse.Create(plan.Id, courseId));
        }

        plan.RaiseDomainEvent(new PlanCreatedDomainEvent(plan.Id, authorId, tier));
        return plan;
    }

    /// <summary>
    /// Заменяет набор курсов плана (bundle, #404). Валиден только для <c>COURSE</c>-tier;
    /// дедуп; требует ≥1 курс. Для каждого добавленного/удалённого курса, ПОКА план в каталоге,
    /// рейзит <see cref="PlanCourseBoundDomainEvent"/> / <see cref="PlanCourseUnboundDomainEvent"/>,
    /// чтобы ECS пересчитал кеш цены курса.
    /// </summary>
    public UnitResult<Error> SetCourses(IReadOnlyList<Guid> courseIds)
    {
        if (Tier != PlanTier.COURSE)
        {
            return AccessErrors.CourseForbiddenForTier();
        }

        IReadOnlyList<Guid> desired = (courseIds ?? []).Distinct().ToList();
        if (desired.Count == 0)
        {
            return AccessErrors.CourseRequiredForCourseTier();
        }

        HashSet<Guid> current = [.. _courses.Select(c => c.CourseId)];
        HashSet<Guid> target = [.. desired];

        IReadOnlyList<Guid> toRemove = current.Where(id => !target.Contains(id)).ToList();
        IReadOnlyList<Guid> toAdd = target.Where(id => !current.Contains(id)).ToList();

        bool inCatalog = IsInCatalog();

        foreach (Guid courseId in toRemove)
        {
            _courses.RemoveAll(c => c.CourseId == courseId);
            if (inCatalog)
            {
                RaiseDomainEvent(new PlanCourseUnboundDomainEvent(Id, courseId));
            }
        }

        foreach (Guid courseId in toAdd)
        {
            _courses.Add(PlanCourse.Create(Id, courseId));
            if (inCatalog)
            {
                RaiseDomainEvent(new PlanCourseBoundDomainEvent(
                    Id, AuthorId, courseId, PriceCents, Currency, IsActive, IsPublic));
            }
        }

        return UnitResult.Success<Error>();
    }

    public void UpdateDescription(string? shortDesc, string? longDesc)
    {
        ShortDescription = shortDesc ?? string.Empty;
        LongDescription = longDesc ?? string.Empty;
    }

    public void UpdateDisplayName(PlanDisplayName name) => DisplayName = name;

    /// <summary>
    ///     Меняет маркетинг-формат оффера. Tier-валидация та же, что в <see cref="Create"/>:
    ///     для FULL_ALL/LEARN_ALL — forced <c>FULL_ACCESS</c> (вход игнорируется, no-op);
    ///     для COURSE — COURSE/INTENSIVE/MARATHON (FULL_ACCESS отвергается).
    /// </summary>
    public UnitResult<Error> UpdateOfferType(PlanOfferType offerType)
    {
        Result<PlanOfferType, Error> resolved = ResolveOfferType(Tier, offerType);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        OfferType = resolved.Value;
        Scope = ResolveScope(OfferType);
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Каталожный scope как чистая производная от offer-type'а (#674): <c>TRAINER_PRO</c> →
    ///     изолированный <see cref="PlanScope.TRAINER"/>, любой другой оффер → платформенный
    ///     <see cref="PlanScope.PLATFORM"/>. Единая точка вывода — не дублировать в callers.
    /// </summary>
    private static PlanScope ResolveScope(PlanOfferType offerType) =>
        offerType == PlanOfferType.TRAINER_PRO ? PlanScope.TRAINER : PlanScope.PLATFORM;

    /// <summary>
    ///     Резолвит маркетинг-offer-type против tier'а. FULL_ALL/LEARN_ALL — forced
    ///     <c>FULL_ACCESS</c> (любой вход игнорируется). SUBSCRIPTION — forced
    ///     <c>TRAINER_PRO</c> (вход тоже игнорируется, #614). COURSE — null дефолтит в
    ///     <c>COURSE</c>, COURSE/INTENSIVE/MARATHON проходят, FULL_ACCESS отвергается.
    ///     Прочее — <c>COURSE</c> для полноты (Create всё равно их отвергает).
    /// </summary>
    private static Result<PlanOfferType, Error> ResolveOfferType(PlanTier tier, PlanOfferType? offerType) =>
        tier switch
        {
            PlanTier.FULL_ALL or PlanTier.LEARN_ALL => PlanOfferType.FULL_ACCESS,
            PlanTier.SUBSCRIPTION => PlanOfferType.TRAINER_PRO,
            PlanTier.COURSE => offerType switch
            {
                null => PlanOfferType.COURSE,
                PlanOfferType.COURSE or PlanOfferType.INTENSIVE or PlanOfferType.MARATHON => offerType.Value,
                _ => AccessErrors.OfferTypeInvalidForTier(),
            },
            _ => offerType ?? PlanOfferType.COURSE,
        };

    /// <summary>
    /// Устанавливает/очищает Telegram-приветствие плана. Пустая строка / whitespace → <c>null</c>
    /// (очистка). Обрезается по краям. Валидация длины — на уровне команды (макс. 4096).
    /// </summary>
    public void UpdateTelegramWelcomeMessage(string? message) =>
        TelegramWelcomeMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

    public void UpdateFeatures(IReadOnlyList<string>? features) => Features = features ?? [];

    public void UpdateCover(Guid? fileId) => CoverFileId = fileId;

    public void UpdatePrice(long? cents, string? currency)
    {
        PriceCents = cents;
        Currency = string.IsNullOrWhiteSpace(currency) ? "RUB" : currency;

        // Акция на план без цены лишена смысла — снимаем её, если цену обнулили.
        // Если цена осталась положительной, процентная скидка переприменяется к новой базе.
        if (PriceCents is null or <= 0)
        {
            ClearPromotion();
        }

        RaiseCourseBoundIfInCatalog();
    }

    /// <summary>
    /// Устанавливает (или заменяет) акцию. Валидирует: у плана есть положительная цена,
    /// процент 1..99, <paramref name="endsAt"/> позже <paramref name="startsAt"/> и в будущем
    /// относительно <paramref name="now"/>. <paramref name="startsAt"/> в прошлом допустим —
    /// акция уже идёт.
    /// </summary>
    public UnitResult<Error> SetPromotion(
        int percent,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now)
    {
        if (PriceCents is null or <= 0)
        {
            return AccessErrors.PromotionRequiresPrice();
        }

        if (percent is < 1 or > 99)
        {
            return AccessErrors.PromotionPercentInvalid();
        }

        if (endsAt <= startsAt)
        {
            return AccessErrors.PromotionWindowInvalid();
        }

        if (endsAt <= now)
        {
            return AccessErrors.PromotionWindowInPast();
        }

        DiscountPercent = percent;
        DiscountStartsAt = startsAt;
        DiscountEndsAt = endsAt;

        // Для COURSE-плана в каталоге дёргаем bound-событие, чтобы ECS сбросил
        // кеш цены курса (`access:plan-for-course:{CourseId}`) и подхватил новую акцию.
        RaiseCourseBoundIfInCatalog();
        return UnitResult.Success<Error>();
    }

    public void ClearPromotion()
    {
        DiscountPercent = null;
        DiscountStartsAt = null;
        DiscountEndsAt = null;
        RaiseCourseBoundIfInCatalog();
    }

    /// <summary>
    /// Активна ли акция в момент <paramref name="now"/> — все поля заданы, цена положительна,
    /// и <c>now ∈ [startsAt; endsAt)</c>. Чистая функция, читается из любого DTO-маппинга.
    /// </summary>
    public bool IsPromotionActive(DateTimeOffset now) =>
        DiscountPercent is >= 1 and <= 99
        && DiscountStartsAt is { } startsAt
        && DiscountEndsAt is { } endsAt
        && now >= startsAt
        && now < endsAt
        && PriceCents is > 0;

    /// <summary>
    /// Цена с учётом активной акции на момент <paramref name="now"/>. Если акции нет/неактивна —
    /// возвращает обычную <see cref="PriceCents"/>. Целочисленная арифметика (truncate);
    /// цены платформы — круглые, потеря копеек несущественна.
    /// </summary>
    public long? EffectivePriceCents(DateTimeOffset now)
    {
        if (PriceCents is not { } price)
        {
            return null;
        }

        if (!IsPromotionActive(now))
        {
            return price;
        }

        long discounted = price - (price * DiscountPercent!.Value / 100);
        return Math.Max(0, discounted);
    }

    public void UpdateDisplayOrder(int order) => DisplayOrder = order;

    /// <summary>
    ///     Изменение capabilities. Для singleton-tier'ов FREE/LEARN_ALL/FULL_ALL —
    ///     forced default (no-op для случайно переданных значений); для COURSE/SUBSCRIPTION
    ///     — author-настраиваемое.
    /// </summary>
    public void UpdateCapabilities(PlanCapabilities capabilities)
    {
        Capabilities = Tier switch
        {
            PlanTier.FREE or PlanTier.LEARN_ALL => PlanCapabilities.VIEW_MATERIALS,
            PlanTier.FULL_ALL => PlanCapabilities.FULL,
            _ => capabilities,
        };
    }

    public void UpdateIsHighlighted(bool isHighlighted) => IsHighlighted = isHighlighted;

    public void Publish()
    {
        if (IsPublic)
        {
            return;
        }

        IsPublic = true;
        RaiseCourseBoundIfInCatalog();
    }

    public void Unpublish()
    {
        if (!IsPublic)
        {
            return;
        }

        bool wasInCatalog = IsInCatalog();
        IsPublic = false;

        if (wasInCatalog)
        {
            foreach (PlanCourse course in _courses)
            {
                RaiseDomainEvent(new PlanCourseUnboundDomainEvent(Id, course.CourseId));
            }
        }
    }

    /// <summary>
    /// Привязывает план к GitHub-org. Slug нормализуется (trim + lowercase).
    /// Поднимает <see cref="PlanGithubOrgChangedDomainEvent"/> при любом изменении —
    /// и при set (непустой), и при unset (null). Consumers (см. event docs) auto-sync'ят
    /// onboarding GITHUB step и retro-grant'ы для уже-залогиненных юзеров org.
    /// </summary>
    public UnitResult<Error> UpdateGithubOrg(string? slug)
    {
        string? normalized = NormalizeGithubOrg(slug);
        if (normalized is not null && !IsValidGithubOrgSlug(normalized))
        {
            return AccessErrors.PlanGithubOrgInvalid();
        }

        if (string.Equals(GitHubOrg, normalized, StringComparison.Ordinal))
        {
            return UnitResult.Success<Error>();
        }

        GitHubOrg = normalized;
        RaiseDomainEvent(new PlanGithubOrgChangedDomainEvent(Id, AuthorId, normalized));

        return UnitResult.Success<Error>();
    }

    private static string? NormalizeGithubOrg(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return slug.Trim().ToLowerInvariant();
    }

    // GitHub login slug rules: 1–39 chars, alphanumeric + single dashes, can't start/end with dash.
    private static bool IsValidGithubOrgSlug(string value)
    {
        if (value.Length is 0 or > 39)
        {
            return false;
        }

        if (value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        bool prevDash = false;
        foreach (char c in value)
        {
            bool alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            bool dash = c == '-';
            if (!alnum && !dash)
            {
                return false;
            }

            if (dash && prevDash)
            {
                return false;
            }

            prevDash = dash;
        }

        return true;
    }

    public UnitResult<Error> Archive()
    {
        if (ArchivedAt is not null)
        {
            return AccessErrors.PlanArchived();
        }

        // Narrow the Unbound event to plans that were actually in the catalog
        // (active + public + COURSE-bound). Archiving a never-public draft does
        // not need a cache-invalidation event on consumers — the key was never
        // populated. Matches the semantics already used by Unpublish().
        bool wasInCatalog = IsInCatalog();
        ArchivedAt = DateTimeOffset.UtcNow;
        IsActive = false;
        IsPublic = false;

        if (wasInCatalog)
        {
            foreach (PlanCourse course in _courses)
            {
                RaiseDomainEvent(new PlanCourseUnboundDomainEvent(Id, course.CourseId));
            }
        }

        return UnitResult.Success<Error>();
    }

    public void Unarchive()
    {
        if (ArchivedAt is null)
        {
            return;
        }

        ArchivedAt = null;
        IsActive = true;
        RaiseCourseBoundIfInCatalog();
    }

    private bool IsCourseBindable() => Tier == PlanTier.COURSE && _courses.Count > 0;

    private bool IsInCatalog() => IsCourseBindable() && IsActive && IsPublic;

    private void RaiseCourseBoundIfInCatalog()
    {
        if (!IsInCatalog())
        {
            return;
        }

        foreach (PlanCourse course in _courses)
        {
            RaiseDomainEvent(new PlanCourseBoundDomainEvent(
                Id,
                AuthorId,
                course.CourseId,
                PriceCents,
                Currency,
                IsActive,
                IsPublic));
        }
    }
}
