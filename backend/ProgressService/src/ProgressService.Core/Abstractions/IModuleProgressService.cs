namespace ProgressService.Core.Abstractions;

/// <summary>
///     Сервис управления прогрессом по модулям.
///     Оркестрирует создание ModuleItemProgress и обновление ModuleProgress.
/// </summary>
public interface IModuleProgressService
{
    Task<UnitResult<Error>> CompleteIssueModuleItemAsync(
        Guid enrollmentId,
        Guid issueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает отметку завершённости с module item по задаче. Декремент <c>ModuleProgress.ItemsCompleted</c>
    /// и откат статуса COMPLETED → IN_PROGRESS делается атомарно. Идемпотентно: если item уже
    /// NOT_COMPLETED или не существует — возвращает успех без действий.
    /// </summary>
    Task<UnitResult<Error>> UncompleteIssueModuleItemAsync(
        Guid enrollmentId,
        Guid issueId,
        CancellationToken cancellationToken = default);

    Task<UnitResult<Error>> CompleteMaterialModuleItemAsync(
        Guid enrollmentId,
        Guid moduleId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Закрывает module item по квизу (item_type=QUIZ) — зеркало
    ///     <see cref="CompleteMaterialModuleItemAsync"/>. Идемпотентно: уже существующий
    ///     item не дублируется (повторная passed-попытка — no-op). Вызывается каскадом
    ///     <c>CompleteQuizModuleItemOnAttemptPassed</c> (ST-13 #493).
    /// </summary>
    Task<UnitResult<Error>> CompleteQuizModuleItemAsync(
        Guid enrollmentId,
        Guid moduleId,
        Guid quizId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает отметку завершённости с module item по материалу. Декремент <c>ModuleProgress.ItemsCompleted</c>
    /// и откат статуса COMPLETED → IN_PROGRESS делается атомарно. Идемпотентно: если item уже
    /// NOT_COMPLETED или не существует — возвращает успех без действий. Используется при unmark
    /// материала пользователем во всех его активных enrollment'ах, где материал есть.
    /// </summary>
    Task<UnitResult<Error>> UncompleteMaterialModuleItemAsync(
        Guid enrollmentId,
        Guid materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Гарантирует наличие <c>ModuleProgress</c> для пары (enrollmentId, moduleId).
    ///     Вызывается каскадом при просмотре материала, чтобы ленивая инициализация модуля
    ///     происходила при первом взаимодействии, а не при записи на курс.
    /// </summary>
    Task<UnitResult<Error>> EnsureModuleProgressAsync(
        Guid enrollmentId,
        Guid moduleId,
        int moduleItemsTotal,
        CancellationToken cancellationToken = default);
}
