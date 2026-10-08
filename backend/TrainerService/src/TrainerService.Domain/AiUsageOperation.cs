namespace TrainerService.Domain;

/// <summary>
/// Тип AI-операции, чья стоимость пишется в leджер использования (#614 C1).
/// OPEN_ANSWER_GRADE — грейдинг одного открытого ответа (LLM, инлайн или фоновый мок-грейд).
/// MOCK_AGGREGATE — агрегатный AI-фидбэк по итогам мок-собеседования (LLM).
/// TRANSCRIPTION — распознавание голосового ответа (STT/Whisper).
/// </summary>
public enum AiUsageOperation
{
    OPEN_ANSWER_GRADE,
    MOCK_AGGREGATE,
    TRANSCRIPTION,
}
