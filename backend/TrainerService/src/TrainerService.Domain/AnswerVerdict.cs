namespace TrainerService.Domain;

/// <summary>
/// Вердикт грейдера по ответу на вопрос / Grader verdict for a single answer.
/// PENDING — ответ ещё не оценён (async AI-грейдинг). CORRECT/PARTIAL/INCORRECT — финальные.
/// </summary>
public enum AnswerVerdict
{
    CORRECT,
    PARTIAL,
    INCORRECT,
    PENDING,
}
