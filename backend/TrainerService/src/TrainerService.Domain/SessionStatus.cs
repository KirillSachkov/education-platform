namespace TrainerService.Domain;

/// <summary>
/// Жизненный цикл тренировочной сессии / Training session lifecycle.
/// IN_PROGRESS → COMPLETED (финализирована) или ABANDONED (брошена).
/// </summary>
public enum SessionStatus
{
    IN_PROGRESS,
    COMPLETED,
    ABANDONED,
}
