namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Статус AI-грейдинга открытых вопросов level-test попытки (ST-4, #479).
///     Хранится строкой (varchar(50), без CHECK) — новый статус добавляется без миграции.
///     NONE — открытых ответов нет, грейдить нечего; QUEUED — попытка ждёт AI-грейдер
///     (опубликован <c>GradeLevelTestAttemptRequested</c>, handler — ST-5); GRADING —
///     грейдер взял в работу; READY — AI-баллы применены, тоталы пересчитаны с
///     open_text в знаменателях; FAILED — AI не справился, действуют choice-only проценты.
/// </summary>
public enum LevelTestAiGradingStatus
{
    NONE,
    QUEUED,
    GRADING,
    READY,
    FAILED,
}
