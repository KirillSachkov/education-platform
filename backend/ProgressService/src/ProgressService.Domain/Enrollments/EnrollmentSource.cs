namespace ProgressService.Domain.Enrollments;

/// <summary>
///     Источник зачисления — для аудита, поиска утечек доступа и групповых reverse-операций.
///     <list type="bullet">
///         <item><c>SELF_PURCHASE</c> — самостоятельная запись (включая TRIAL на курсах с FREE-контентом).</item>
///         <item><c>ADMIN_GRANT</c> — выдано админом / автором курса (batch enroll, ручное приглашение).</item>
///         <item><c>GITHUB_ORG</c> — авто-зачисление по членству в GitHub-организации курса.</item>
///         <item><c>TELEGRAM_CHAT</c> — выдано через Telegram-бот по членству в bound-чате (F3 reverse-флоу).</item>
///         <item><c>ACCESS_PLAN_GRANT</c> — legacy: материализовано из <c>plan_grant.created</c> AccessService'а до access-derive-model (#367). Новые grant'ы enrollment больше не материализуют — остаётся только на исторических строках.</item>
///         <item><c>ENGAGEMENT</c> — ленивый progress-anchor (access-derive-model Phase 2). Запись создаётся «по требованию»
///             на первом взаимодействии entitled-пользователя с курсом (mark viewed / submit / start). Доступ при этом
///             определяется AccessService PlanGrant'ами, а не этой записью; строка нужна только как FK-родитель прогресса.
///             Событий не публикует.</item>
///         <item><c>AUTHOR_SELF</c> — автор курса видит свой курс в «моих курсах» и лидерборде (pre-seed на <c>course.created</c>).</item>
///         <item><c>UNKNOWN</c> — pre-existing rows до введения source-tracking; не для use в новом коде.</item>
///     </list>
/// </summary>
public enum EnrollmentSource
{
    UNKNOWN = 0,
    SELF_PURCHASE = 1,
    ADMIN_GRANT = 2,
    GITHUB_ORG = 3,
    TELEGRAM_CHAT = 4,
    ACCESS_PLAN_GRANT = 5,

    /// <summary>Lazy progress-anchor created on first entitled engagement (Phase 2). No events.</summary>
    ENGAGEMENT = 6,

    /// <summary>Author auto-enroll on course creation (sees own course in my-courses / leaderboard).</summary>
    AUTHOR_SELF = 7
}
