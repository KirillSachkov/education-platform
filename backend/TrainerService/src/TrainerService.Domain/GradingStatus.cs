namespace TrainerService.Domain;

/// <summary>
///     Статус AI-грейдинга мок-собес сессии (#585). Авто-грейдимые тесты (DRILL/LEARN/track-MOCK)
///     детерминированы и в AI-грейдинге не нуждаются → <see cref="NOT_REQUIRED"/>. Named mock-interview
///     с открытыми (OPEN_TEXT) ответами после Complete уходит в очередь: PENDING → GRADING → GRADED.
///     <see cref="FAILED"/> — неустранимый сбой AI-грейдера (фронт показывает баллы авто-вопросов без AI-фидбэка).
/// </summary>
public enum GradingStatus
{
    NOT_REQUIRED,
    PENDING,
    GRADING,
    GRADED,
    FAILED,
}
