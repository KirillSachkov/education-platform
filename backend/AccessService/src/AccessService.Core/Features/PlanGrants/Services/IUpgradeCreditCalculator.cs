using AccessService.Domain;

namespace AccessService.Core.Features.PlanGrants.Services;

/// <summary>
///     Domain service: расчёт credit (скидки) при покупке плана. Issue #112 / Phase 2.
///
///     <para><b>Subset rule:</b> credit = Σ по покрытым планам (grants группируются по плану —
///     анти-double-count) пользователя, scope которых
///     полностью покрывается scope'ом target-плана: оплаченный план кредитует
///     Σ price_paid_cents его grants; неоплаченный, но ACTIVE (инвайт / Telegram-бот /
///     GitHub-org / admin-grant) — текущую эффективную цену покрытого плана (#486, обещание
///     FAQ «у вас уже есть — доплатите разницу»).</para>
///
///     <para><b>Tier hierarchy для проверки subset:</b></para>
///     <list type="bullet">
///       <item><c>FULL_ALL</c> covers всю платформу — credit'ятся FREE/LEARN_ALL/COURSE/SUBSCRIPTION grants.</item>
///       <item><c>LEARN_ALL</c> covers VIEW_MATERIALS scope платформы — credit'ятся FREE и
///         LEARN-only COURSE-grants. COURSE с full caps не subset (имеет SUBMIT/etc).</item>
///       <item><c>COURSE</c> covers свой <c>course_ids</c> set — credit'ятся FREE и
///         COURSE-grants с <c>course_ids ⊆ target.course_ids</c> + capabilities ⊆ target.</item>
///       <item><c>FREE</c> — только бесплатный, credit всегда 0.</item>
///       <item><c>SUBSCRIPTION</c> — TBD (Phase 3).</item>
///     </list>
///
///     <para><b>Global full-access rule:</b> FULL_ALL / LEARN_ALL больше не author-scoped;
///     credit для покупки global-плана может прийти от покрытых планов любого автора.</para>
///
///     <para><b>Status filter:</b> REVOKED (включая refunded) credit не даёт — после возврата
///     денег скидка исчезает (#414, owner decision). EXPIRED с оплатой — даёт (деньги внесены
///     и не возвращались); EXPIRED без оплаты — нет (юзер ничем не владеет).</para>
///
///     <para><b>Cap:</b> итоговый <c>credit ≤ original_price</c> — переплата не возвращается.</para>
/// </summary>
public interface IUpgradeCreditCalculator
{
    /// <summary>
    ///     Вычисляет upgrade-quote для пары (user, targetPlan).
    /// </summary>
    Task<UpgradeQuote> CalculateAsync(
        Guid userId,
        Plan targetPlan,
        CancellationToken ct);
}

/// <summary>
///     Результат расчёта credit'а. Маппится в <c>UpgradeQuoteDto</c> на endpoint'е.
/// </summary>
public sealed record UpgradeQuote(
    long? OriginalPriceCents,
    long CreditCents,
    long? FinalPriceCents,
    bool IsOwned,
    IReadOnlyList<UpgradeCreditSource> Sources);

/// <summary>
///     Один grant, давший credit. <see cref="PlanDisplayName"/> для UI breakdown'а.
/// </summary>
public sealed record UpgradeCreditSource(
    Guid GrantId,
    Guid PlanId,
    string PlanDisplayName,
    PlanTier PlanTier,
    long CreditCents);
