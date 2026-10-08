namespace AccessService.Domain;

/// <summary>
///     Где живёт оффер плана: в общем каталоге платформы (<see cref="PLATFORM"/>) или в
///     изолированном «магазине» тренажёра (<see cref="TRAINER"/>). Дискриминатор-производная
///     от <see cref="PlanOfferType"/>: <c>TRAINER_PRO → TRAINER</c>, всё остальное → <c>PLATFORM</c>
///     (единственный source-of-truth — <see cref="Plan"/> устанавливает <c>Scope</c> в factory и
///     при смене offer-type'а, callers не задают его напрямую).
///
///     <para>Назначение (#674): TRAINER-планы продаются как обычный <see cref="Plan"/> поверх той же
///     order/grant-машинерии, но НЕ должны попадать в платформенный каталог/pricing и «полный
///     доступ»-бандл. Catalog/pricing read-пути фильтруют <c>Scope == PLATFORM</c>; покупка и
///     управление TRAINER-оффером живут на выделенных <c>/access/trainer-pro/*</c> эндпоинтах.</para>
/// </summary>
public enum PlanScope
{
    /// <summary>Оффер общего каталога платформы — виден в <c>/pricing</c> и «полном доступе».</summary>
    PLATFORM,

    /// <summary>Оффер тренажёра (Trainer Pro). Скрыт из платформенного каталога; продаётся/управляется
    /// только через <c>/access/trainer-pro/*</c>.</summary>
    TRAINER,
}
