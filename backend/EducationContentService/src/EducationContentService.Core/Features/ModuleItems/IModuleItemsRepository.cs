using EducationContentService.Domain.Modules;
using Ordering;

namespace EducationContentService.Core.Features.ModuleItems;

public interface IModuleItemsRepository : IOrderedItemsRepository<ModuleItem>
{
    /// <summary>
    ///     Verifies that the Issue is not yet attached to any module.
    ///     Invariant: one Issue can belong to only one ModuleItem.
    /// </summary>
    Task<UnitResult<Error>> CheckIssueNotAttachedAsync(
        Guid issueId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Остались ли у квиза другие module_items в модулях указанного курса, кроме
    ///     исключаемого (ST-12 #492). Питает derived-инвариант <c>course_quizzes</c>:
    ///     строка (course, quiz) живёт, пока квиз размещён хотя бы в одном модуле курса —
    ///     detach/transfer последнего item'а удаляет привязку и сужает Redis-теги.
    ///     Запрос идёт в БД, поэтому удаляемый item (ещё не flushed) исключается по id.
    /// </summary>
    Task<bool> HasOtherQuizItemsInCourseAsync(
        Guid courseId, Guid quizId, Guid excludedItemId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Есть ли в модулях курса хоть один issue-item. Гейтит смену <c>Course.Kind</c>
    ///     на INTENSIVE/MARATHON — у этих типов в модулях не должно быть заданий
    ///     (зеркало запрета в <c>AttachIssueToModule</c>).
    /// </summary>
    Task<bool> HasIssueItemsInCourseAsync(Guid courseId, CancellationToken cancellationToken = default);
}
