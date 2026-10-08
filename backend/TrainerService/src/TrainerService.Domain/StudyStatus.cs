namespace TrainerService.Domain;

/// <summary>
/// Статус изучения вопроса пользователем / Per-user per-question study status (Ф2, #568).
/// Отсутствие строки <c>QuestionStudyState</c> = «новый» (NEW) вопрос — не хранится отдельным членом.
/// SEEN — видел (флеш-карта раскрыта). KNOWN — усвоен (успешная самооценка / верный ответ, SRS-success).
/// REVIEW — отложен на повтор по SRS. WRONG — ошибся (SRS reset).
/// </summary>
public enum StudyStatus
{
    SEEN,
    KNOWN,
    REVIEW,
    WRONG,
}
