namespace AccessService.Domain;

/// <summary>
///     Tier плана — first-class concept, определяет UI-категорию + scope доступа +
///     singleton-правила + default capabilities. Заменяет старый <see cref="PlanKind"/>.
///
///     <para>Singleton-правила (в БД через partial unique index):</para>
///     <list type="bullet">
///       <item><see cref="FREE"/> deprecated; новые планы не создаются.</item>
///       <item>Один <see cref="LEARN_ALL"/> на платформу (active+public+not-archived).</item>
///       <item>Один <see cref="FULL_ALL"/> на платформу (active+public+not-archived).</item>
///       <item><see cref="COURSE"/> и <see cref="SUBSCRIPTION"/> — без singleton.</item>
///     </list>
///
///     <para>Domain-инварианты в <c>Plan.Create</c> / <c>UpdatePlan</c>:</para>
///     <list type="bullet">
///       <item><see cref="FREE"/>: capabilities принудительно <c>VIEW_MATERIALS</c>, <c>course_ids</c>
///         запрещены (legacy FREE открывает только старый FREE-контент).</item>
///       <item><see cref="LEARN_ALL"/>: capabilities принудительно <c>VIEW_MATERIALS</c>, <c>course_ids</c>
///         запрещены (только просмотр всего контента платформы).</item>
///       <item><see cref="FULL_ALL"/>: capabilities принудительно ВСЕ
///         (<c>VIEW_MATERIALS | SUBMIT_ISSUES | CODE_REVIEW | COMMUNITY_ACCESS | LIVE_CALLS | JOB_SUPPORT</c>),
///         <c>course_ids</c> запрещены.</item>
///       <item><see cref="COURSE"/>: <c>course_ids.Count &gt; 0</c>, capabilities на выбор автора
///         (от LEARN_ONLY до FULL для конкретного курса).</item>
///       <item><see cref="SUBSCRIPTION"/>: не привязан к курсам (<c>course_ids</c> запрещены),
///         обязан иметь периодический <c>PlanTerm</c> (RECURRING + положительный интервал, иначе
///         <c>plan.subscription.requires_recurring_term</c>); capabilities на выбор автора
///         (default <c>TRAINER_PRO</c>). Auto-renew billing — #614.</item>
///     </list>
/// </summary>
public enum PlanTier
{
    /// <summary>
    ///     FREE — deprecated. Бесплатный доступ теперь = system default (любой залогиненный
    ///     юзер) через <c>AccessType=REGISTERED</c>. Создавать новые планы этого tier нельзя
    ///     (<c>Plan.Create</c> возвращает <c>plan.free.deprecated</c>); существующие
    ///     archived data-миграцией #358. Enum-значение оставлено для legacy-строк в БД.
    /// </summary>
    FREE,

    /// <summary>
    ///     «Только материалы» / «Наблюдатель». Полный просмотр всего контента платформы
    ///     без practice / community / mentor-feedback. Singleton на платформу.
    /// </summary>
    LEARN_ALL,

    /// <summary>
    ///     «Полный доступ». Все материалы + задания + код-ревью + сообщество + созвоны
    ///     по всей платформе. Singleton на платформу.
    /// </summary>
    FULL_ALL,

    /// <summary>
    ///     План на конкретный курс или подборку курсов. Capabilities автор задаёт сам
    ///     (можно от read-only до full per-course). Multiple на платформу.
    /// </summary>
    COURSE,

    /// <summary>
    ///     Подписочный план с авто-продлением (#614). Не привязан к курсам; требует
    ///     периодический <c>PlanTerm</c> (RECURRING). Default capability — <c>TRAINER_PRO</c>.
    ///     Recurring charge/renewal-логика (T-Bank rebill) — workstream A2. Multiple на платформу.
    /// </summary>
    SUBSCRIPTION,
}
