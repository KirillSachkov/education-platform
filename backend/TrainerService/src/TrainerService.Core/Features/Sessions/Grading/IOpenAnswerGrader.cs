using Shared.AI;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     Итог AI-грейдинга одного открытого ответа: вердикт + балл (0..100) + краткий фидбэк.
///     <para>
///         <see cref="Usage"/> + <see cref="Model"/> (#614 C1) несут токены/модель ОДНОГО LLM-вызова —
///         для записи в лоджер AI-использования на стороне caller'а (грейдер остаётся context-free, не
///         знает про userId/sessionId). <see cref="Usage"/> null = вызова LLM не было (пустой ответ →
///         INCORRECT/0 без вызова) или провайдер не вернул usage.
///     </para>
/// </summary>
public readonly record struct OpenAnswerGrade(
    AnswerVerdict Verdict,
    int ScorePercent,
    string? Feedback,
    AiUsage? Usage = null,
    string Model = "");

/// <summary>
///     Переиспользуемый AI-грейдер ОДНОГО открытого ответа (OPEN_TEXT). Один и тот же промпт +
///     JSON-схема (<c>trainer_open_answer_grade</c>, #585) используется и фоновым мок-грейдером
///     (<see cref="MockAnswerGradingService"/>, после Complete), и инлайновым грейдингом открытых
///     ответов в не-мок сессиях (LEARN/DRILL/тест), чтобы промпт не дублировался.
/// </summary>
public interface IOpenAnswerGrader
{
    /// <summary>
    ///     Оценивает один открытый ответ относительно эталона. Пустой ответ → INCORRECT/0 без вызова
    ///     LLM. Иначе — структурированный AI-вердикт. На любом сбое/таймауте возвращает
    ///     <c>Result.Failure</c> (caller сам решает, как деградировать) — НЕ кидает исключение.
    /// </summary>
    /// <param name="questionStem">Текст вопроса.</param>
    /// <param name="referenceAnswer">Эталонный ответ (может быть null — тогда оценивается по смыслу вопроса).</param>
    /// <param name="studentText">Ответ студента (печатный или распознанный из речи).</param>
    Task<Result<OpenAnswerGrade, Error>> GradeAsync(
        string questionStem,
        string? referenceAnswer,
        string? studentText,
        CancellationToken ct);
}
