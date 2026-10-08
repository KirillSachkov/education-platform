namespace AccessService.Domain;

/// <summary>
/// Источник создания grant'а / Origin that created the grant.
/// </summary>
public enum PlanGrantSource
{
    /// <summary>
    /// Создан через redeem инвайт-ссылки / Created by invite link redemption.
    /// </summary>
    INVITE_LINK,

    /// <summary>
    /// Выдан вручную админом/автором / Manually granted by admin/author.
    /// </summary>
    ADMIN_GRANT,

    /// <summary>
    /// Создан backfill-миграцией из legacy enrollment'ов /
    /// Created by backfill migration from legacy enrollments.
    /// </summary>
    MIGRATION,

    /// <summary>
    /// Создан после успешной покупки (dormant до интеграции с биллингом) /
    /// Created after a successful purchase (dormant pending billing integration).
    /// </summary>
    PURCHASE,

    /// <summary>
    /// Создан, когда пользователь самостоятельно claim'ит бесплатный план
    /// (нажал «Получить бесплатно» на странице плана). Выдаётся ровно один
    /// раз на пару (user, plan) — даже после revoke повторный claim не разрешён.
    /// Бессрочный (без TTL); зарезервированное имя <c>TRIAL</c> сохранено для
    /// обратной совместимости — в будущем может разделиться на <c>FREE_CLAIM</c>
    /// и <c>TRIAL</c> (с TTL).
    /// Self-served free plan claim. One-shot per (user, plan), permanent.
    /// </summary>
    TRIAL,

    /// <summary>
    /// Создан при GitHub org match — юзер залогинился и состоит в org, привязанной
    /// к плану. Выдаёт matched plan grant. Wired Phase C (#43).
    /// Created on GitHub org match — issues the matched plan grant.
    /// </summary>
    GITHUB_ORG,

    /// <summary>
    /// Создан при Telegram F1 trigger (deep-link / chat-binding). Reserved для
    /// будущей миграции F1 flow на plan-grants; текущая F1 продолжает идти через
    /// CourseEnrollment до Phase E cutover.
    /// Reserved for future Telegram F1 → plan-grant migration.
    /// </summary>
    TELEGRAM_F1,
}
