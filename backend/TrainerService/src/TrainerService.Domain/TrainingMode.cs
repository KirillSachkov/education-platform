namespace TrainerService.Domain;

/// <summary>
/// Режим тренировочной сессии / Training session mode.
/// DRILL — random-N вопросов по теме с instant-check (Ф1).
/// LEARN — formative «обучение по тестам»: мгновенный фидбэк, ошибочные повторяются до усвоения,
///   без записываемого балла (Ф2, #568).
/// MOCK — симуляция собеса: микс тем + финальный AI-вердикт (Ф2).
/// CHALLENGE — ежедневный набор из слабых тем + таймер + XP (Ф3).
/// </summary>
public enum TrainingMode
{
    DRILL,
    LEARN,
    MOCK,
    CHALLENGE,
}
