namespace AccessService.Contracts.Plans.Requests;

/// <summary>
/// Запрос на обновление плана доступа /
/// Update-plan command body. Все поля опциональны: <c>null</c> означает «не менять».
/// Pricing pair (PriceCents/Currency) трактуется как партиал — отсутствующее
/// значение наследуется от текущего плана. <c>CourseIds</c> (bundle, #404): <c>null</c> =
/// «не менять»; непустой список = заменить набор курсов плана (только для COURSE-tier,
/// требует ≥1 курс). Пустой список при наличии (count==0) трактуется как «не менять».
/// </summary>
/// <param name="CourseIds">
/// Новый набор курсов COURSE-плана (bundle). <c>null</c> — не менять; непустой —
/// заменить через <c>Plan.SetCourses</c>. Для не-COURSE планов передача непустого
/// списка вернёт <c>plan.course_id.forbidden</c>.
/// </param>
/// <param name="Capabilities">
/// Если <c>null</c> — capabilities не меняются. Иначе полный bitmask
/// собирается из переданного списка имён.
/// </param>
/// <param name="IsHighlighted">
/// Если <c>null</c> — флаг не трогается; в обычной модели обновления
/// разрешено явно ставить <c>true/false</c>.
/// </param>
/// <param name="GithubOrgSlug">
/// GitHub-org slug для авто-выдачи plan-grant'а юзерам этого org при логине.
/// Особое значение — пустая строка <c>""</c> означает «снять привязку», тогда как
/// <c>null</c> означает «не менять». Нормализуется в lowercase в домене.
/// </param>
/// <param name="TelegramWelcomeMessage">
/// Приветствие, которое бот постит в привязанную к плану Telegram-группу при входе участника.
/// <c>null</c> = не менять; пустая строка <c>""</c> = очистить. Max 4096 символов.
/// </param>
/// <param name="OfferType">
/// Маркетинг-формат оффера (<c>FULL_ACCESS | COURSE | INTENSIVE | MARATHON</c>).
/// <c>null</c> = не менять. Tier-валидация в домене: FULL_ALL/LEARN_ALL forced FULL_ACCESS;
/// COURSE — COURSE/INTENSIVE/MARATHON; FULL_ACCESS на COURSE-tier отвергается.
/// </param>
public sealed record UpdatePlanRequest(
    string? DisplayName,
    string? ShortDescription,
    string? LongDescription,
    Guid? CoverFileId,
    IReadOnlyList<string>? Features,
    long? PriceCents,
    string? Currency,
    IReadOnlyList<Guid>? CourseIds,
    int? DisplayOrder,
    IReadOnlyList<string>? Capabilities = null,
    bool? IsHighlighted = null,
    string? GithubOrgSlug = null,
    string? TelegramWelcomeMessage = null,
    string? OfferType = null);
