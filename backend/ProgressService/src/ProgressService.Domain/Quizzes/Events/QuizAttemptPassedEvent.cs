using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Quizzes.Events;

/// <summary>
///     Событие: пользователь прошёл квиз (создана попытка с <c>Passed=true</c>). Поднимается
///     на КАЖДОЙ passed-попытке (не только первой) — handler идемпотентен по
///     <c>module_item_progress</c>. Обрабатывает <c>CompleteQuizModuleItemOnAttemptPassed</c>
///     (cascade на module_item_progress во всех курсах, где квиз размещён в модуле и к которым
///     у пользователя есть доступ). Решение владельца (#493): завершение элемента модуля =
///     ПРОХОДНОЙ БАЛЛ, не любая попытка. Без XP в этой итерации.
/// </summary>
public sealed record QuizAttemptPassedEvent(Guid UserId, Guid QuizId) : IDomainEvent;
