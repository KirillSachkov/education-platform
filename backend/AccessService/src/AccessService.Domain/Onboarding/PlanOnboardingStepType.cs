namespace AccessService.Domain.Onboarding;

/// <summary>
///     Тип шага онбординга.
///     <list type="bullet">
///         <item><c>MARKDOWN</c> — автор-edited welcome / гайды (title+body).</item>
///         <item><c>TELEGRAM</c> / <c>GITHUB</c> / <c>NOTIFICATIONS</c> — авто-управляемые шаги,
///             появляются по факту наличия соответствующей интеграции у плана.</item>
///         <item><c>GITHUB_REVIEW_APP</c> (issue #307) — автор добавляет вручную через
///             отдельный toggle endpoint; шаг приглашает юзера установить AssignmentReviewService
///             GitHub App на свои репозитории. Комплитится event'ом
///             <c>vcs_installation.created</c> из ARS либо auto-detect при создании
///             onboarding если у юзера уже есть active installation.</item>
///     </list>
/// </summary>
public enum PlanOnboardingStepType
{
    MARKDOWN,
    TELEGRAM,
    GITHUB,
    NOTIFICATIONS,
    GITHUB_REVIEW_APP,
}
