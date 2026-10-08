using EducationContentService.Contracts.ProgressLookup;
using ProgressService.Domain.Issues;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Shared staff-completion logic (#398, #518): принимает задачу студенту вручную «как если бы
///     он дошёл сам» — строит полную progress-цепочку (lazy enrollment anchor → ProjectProgress →
///     ModuleProgress + ModuleItemProgress если задача в модуле → IssueProgress), создаёт синтетический
///     принятый <c>IssueSubmission</c> и прогоняет обычный approve-каскад (IssueProgress.Approve →
///     COMPLETED + XP/project/module + integration event <c>issue_submission.approved</c>).
///     <para>
///     Используется и <c>MarkIssueCompleteForUserHandler</c> (#398), и
///     <c>SetIssueStatusForUserHandler</c> (#518, ветка target=COMPLETED) — единый источник правды
///     для двухфазного flush'а и синтетического submission'а.
///     </para>
///     <para>
///     Caller отвечает за auth-чек и резолв реального <paramref name="reviewerId"/> ДО вызова
///     (fail-closed на пустой GUID). Сервис делает <c>SaveChangesAsync</c> через
///     <c>ITransactionManager</c> дважды (chain-flush, затем approve-flush) — отдельный
///     <c>SaveChanges</c> в caller'е не нужен.
///     </para>
/// </summary>
public interface IStaffIssueCompletionService
{
    /// <summary>
    ///     Принимает задачу <paramref name="issueId"/> студенту <paramref name="studentId"/> в курсе
    ///     <paramref name="courseId"/>. Идемпотентно: если <c>IssueProgress</c> уже COMPLETED — XP не
    ///     дублируется, approve-event не уходит.
    /// </summary>
    /// <param name="courseDetail">
    ///     Опционально — заранее загруженный course-lookup (AuthorId уже зарезолвен caller'ом для
    ///     auth). Передаётся, чтобы не делать повторный ECS-вызов.
    /// </param>
    Task<UnitResult<Error>> CompleteIssueForUserAsync(
        Guid studentId,
        Guid courseId,
        Guid issueId,
        Guid reviewerId,
        IssueReviewFeedback? feedback,
        CourseDto courseDetail,
        CancellationToken cancellationToken);
}
