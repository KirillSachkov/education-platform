namespace AccessService.Contracts.Plans.Requests;

/// <summary>
/// Запрос на создание плана доступа.
/// Tier (<c>FREE | FULL_ALL | COURSE | SUBSCRIPTION</c>) полностью определяет
/// invariants: для COURSE требуется хотя бы один курс в <c>CourseIds</c>, для остальных —
/// список должен быть пустым/<c>null</c>. Capabilities автор настраивает только для COURSE —
/// для остальных тиров capabilities залиты тиром (FREE → VIEW_MATERIALS, FULL_ALL → FULL).
/// </summary>
/// <param name="CourseIds">
/// Список course id'ов для COURSE-плана (bundle, #404 — заменил singular <c>CourseId</c>).
/// Для прочих тиров должен быть пустым / <c>null</c>. Дедуп на стороне домена.
/// </param>
/// <param name="Capabilities">
/// Опциональный список capability-имён. Применяется ТОЛЬКО для <c>COURSE</c>; для
/// прочих тиров игнорируется. Default для COURSE — <c>VIEW_MATERIALS + SUBMIT_ISSUES</c>.
/// </param>
/// <param name="OfferType">
/// Маркетинг-формат оффера (<c>FULL_ACCESS | COURSE | INTENSIVE | MARATHON</c>) —
/// ортогонален tier'у. <c>null</c> → дефолт по tier'у (FULL_ALL/LEARN_ALL → FULL_ACCESS,
/// COURSE → COURSE). FULL_ACCESS на COURSE-tier отвергается доменом.
/// </param>
/// <param name="IsTrial">
/// Создать пробный план (#595). <c>true</c> → tier форсится в <c>FULL_ALL</c>,
/// offerType — <c>FULL_ACCESS</c>, срок берётся из конфига (<c>Access:TrialDurationDays</c>,
/// default 30) — автор не вводит длительность. Default <c>false</c> (обычный план).
/// </param>
/// <param name="RecurringIntervalDays">
/// Интервал автопродления подписки в днях (#614). Обязателен и должен быть &gt; 0 для
/// <c>Tier=SUBSCRIPTION</c> (домен отвергает иначе); игнорируется для прочих тиров. Позволяет
/// создать подписочный план «Trainer Pro».
/// </param>
public sealed record CreatePlanRequest(
    string Tier,
    string Slug,
    string DisplayName,
    string? ShortDescription,
    string? LongDescription,
    Guid? CoverFileId,
    IReadOnlyList<string>? Features,
    long? PriceCents,
    string? Currency,
    IReadOnlyList<Guid>? CourseIds,
    int? DisplayOrder,
    IReadOnlyList<string>? Capabilities = null,
    bool IsHighlighted = false,
    string? OfferType = null,
    int? TrialDurationDays = null,
    bool IsTrial = false,
    int? RecurringIntervalDays = null);
