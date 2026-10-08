namespace AccessService.Domain;

/// <summary>
///     Маркетинг-формат оффера — как план презентуется в каталоге/landing'е
///     (бейдж, копирайт, визуал). Ортогонален <see cref="PlanTier"/>, который
///     определяет scope доступа (что именно открывается).
///
///     <para>Связь с tier'ом (инвариант в <c>Plan.Create</c> / <c>Plan.UpdateOfferType</c>):</para>
///     <list type="bullet">
///       <item><see cref="PlanTier.FULL_ALL"/> / <see cref="PlanTier.LEARN_ALL"/> →
///         forced <see cref="FULL_ACCESS"/> (offer-type вход игнорируется, как forced capabilities).</item>
///       <item><see cref="PlanTier.COURSE"/> → один из
///         <see cref="COURSE"/> / <see cref="INTENSIVE"/> / <see cref="MARATHON"/>
///         (default <see cref="COURSE"/>); <see cref="FULL_ACCESS"/> на COURSE-tier отвергается.</item>
///       <item><see cref="PlanTier.SUBSCRIPTION"/> → forced <see cref="TRAINER_PRO"/>
///         (offer-type вход игнорируется, как forced capabilities).</item>
///     </list>
/// </summary>
public enum PlanOfferType
{
    /// <summary>«Полный доступ» — flagship-оффер, обычно покрывает FULL_ALL/LEARN_ALL tier.</summary>
    FULL_ACCESS,

    /// <summary>Оффер на конкретный курс или bundle курсов.</summary>
    COURSE,

    /// <summary>Оффер-интенсив (короткий формат без заданий).</summary>
    INTENSIVE,

    /// <summary>Оффер-марафон (групповой формат без заданий).</summary>
    MARATHON,

    /// <summary>Подписка на тренажёр собеседований (Trainer Pro, #614) — отдельный оффер
    /// на pricing-странице, форсится для <see cref="PlanTier.SUBSCRIPTION"/>.</summary>
    TRAINER_PRO,
}
