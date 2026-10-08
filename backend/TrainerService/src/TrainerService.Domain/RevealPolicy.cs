namespace TrainerService.Domain;

/// <summary>
/// Когда раскрывать правильный ответ в тест-сессии / When the correct answer is revealed (Ф2, #568).
/// END_OF_SESSION (default) — счёт копится, разбор показывается после <c>Complete</c> (summative).
/// PER_QUESTION — мгновенный фидбэк на каждый ответ (formative).
/// </summary>
public enum RevealPolicy
{
    END_OF_SESSION,
    PER_QUESTION,
}
