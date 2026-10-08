namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService после успешного создания <c>PlanGrant</c> (redeem invite,
/// admin grant, миграция, покупка). Consumer'ы используют для своей проекции
/// доступа / уведомлений.
/// </summary>
/// <param name="GrantId">ID созданного PlanGrant.</param>
/// <param name="UserId">ID пользователя, получившего доступ.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="PlanTier"><c>FREE | LEARN_ALL | FULL_ALL | COURSE | SUBSCRIPTION</c>.</param>
/// <param name="PlanAuthorId">ID автора плана-владельца. Entitlement scope определяется планом/курсами.</param>
/// <param name="CourseId">
/// Legacy / fallback подсказка: первый course ID из scope плана (для <c>COURSE</c>-плана).
/// <c>null</c> для <c>FULL_ALL / LEARN_ALL / FREE</c>. Оставлен для backward-compat —
/// новые readers должны читать <see cref="EffectiveCourseIds"/>, а не сырое
/// <see cref="CourseId"/> (bundle-план покрывает несколько курсов, #404).
/// </param>
/// <param name="IncludesFutureContent">
/// Если <c>true</c> — план покрывает будущие курсы платформы (LEARN_ALL/FULL_ALL).
/// </param>
/// <param name="Source"><c>INVITE_LINK | ADMIN_GRANT | MIGRATION | PURCHASE | TRIAL | GITHUB_ORG | TELEGRAM_F1</c>.</param>
/// <param name="SourceRef">ID источника (например, <c>InviteLink.Id</c> для INVITE_LINK).</param>
/// <param name="GrantedAt">Время создания (UTC).</param>
/// <param name="ExpiresAt">Опциональный срок действия (UTC); <c>null</c> для бессрочного доступа.</param>
/// <param name="Capabilities">
/// Список имён capability-флагов из <c>PlanCapabilities</c> bitmask (например,
/// <c>["VIEW_MATERIALS", "SUBMIT_ISSUES", ...]</c>). NotificationService и self-consume
/// access-handler используют для гейтинга действий — submit, community, code-review.
/// Опциональный (default empty) для backward-compat.
/// </param>
/// <param name="CourseIds">
/// Полный список course ID'ов из scope <c>COURSE</c>-плана (bundle, #404). <c>null</c> /
/// пустой для <c>FULL_ALL / LEARN_ALL / FREE</c>. Optional + trailing для backward-compat
/// со старыми позиционными конструкторами (в т.ч. иммутабельные migration-файлы). Новые
/// readers читают <see cref="EffectiveCourseIds"/>.
/// </param>
/// <param name="PlanName">
/// Человекочитаемое имя плана (<c>Plan.DisplayName</c>) на момент выдачи гранта.
/// Денормализовано в событие (#445), чтобы consumer мог показать, к какому именно
/// плану/продукту выдан доступ, без обратного HTTP-вызова в AccessService. Optional +
/// trailing для backward-compat со старыми позиционными конструкторами (в т.ч.
/// иммутабельные migration-файлы / тесты); <c>null</c> → consumer показывает fallback.
/// </param>
/// <param name="OfferType">
/// Маркетинг-формат оффера плана (<c>FULL_ACCESS | COURSE | INTENSIVE | MARATHON</c>,
/// см. <c>PlanOfferType</c> в AccessService.Domain) на момент выдачи гранта. Денормализован
/// в событие (#485), чтобы consumer мог подобрать правильное существительное в тексте
/// уведомления («доступ к интенсиву …») без обратного вызова в AccessService. Optional +
/// trailing для backward-compat; <c>null</c> → consumer падает на tier-based fallback.
/// </param>
public sealed record PlanGrantCreated(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    Guid? CourseId,
    bool IncludesFutureContent,
    string Source,
    Guid? SourceRef,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<Guid>? CourseIds = null,
    string? PlanName = null,
    string? OfferType = null)
{
    /// <summary>
    /// Каноничный список course ID'ов scope'а плана. Предпочитает новый bundle-список
    /// <see cref="CourseIds"/>; падает на legacy singular <see cref="CourseId"/>; иначе пусто.
    /// Все NEW readers обязаны читать его, а не сырое <see cref="CourseId"/>.
    /// </summary>
    public IReadOnlyList<Guid> EffectiveCourseIds =>
        CourseIds is { Count: > 0 } ? CourseIds : (CourseId is { } c ? [c] : []);
}
