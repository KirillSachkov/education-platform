using System.Text.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions;

/// <summary>
///     Маппинг сессии/item'ов в студенческие DTO. Гарантирует, что ключ грейдинга
///     (<c>GradingKeyJson</c>) НИКОГДА не утекает: для неотвеченного вопроса correct-поля
///     null; для отвеченного — раскрываются из снапшота ключа (review).
/// </summary>
public static class SessionMapper
{
    /// <summary>camelCase JSON для снапшотов вариантов и ключа грейдинга в jsonb-колонках.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="hasPro">
    ///     Есть ли у вызывающего PRO (или admin). Питает per-item <c>IsLocked</c> монетизации по типу
    ///     вопроса (#623): OPEN_TEXT (развёрнутый/голос → AI) заблокирован для не-PRO; закрытые вопросы
    ///     никогда не locked. Резолвится в хендлере через <see cref="TrainerProAccessPolicy.HasProAsync"/>.
    /// </param>
    public static SessionDto ToDto(TrainingSession session, bool hasPro)
    {
        // Grade-at-end gate (#568 Ф2): для END_OF_SESSION-теста, пока сессия IN_PROGRESS, разбор
        // отвеченных item'ов НЕ раскрывается — счёт копится «вслепую», ответы видны после Complete.
        // PER_QUESTION (formative) и завершённая сессия — раскрывают сразу.
        bool revealAnswered =
            session.RevealPolicy == RevealPolicy.PER_QUESTION
            || session.Status != SessionStatus.IN_PROGRESS;

        var items = session.Items
            .OrderBy(i => i.SortIndex)
            .Select(i => ToItemDto(i, revealAnswered, hasPro))
            .ToList();

        return new SessionDto(
            session.Id,
            session.Mode.ToString(),
            session.Status.ToString(),
            session.RevealPolicy.ToString(),
            session.TopicIds,
            session.TimeLimitSeconds,
            session.StartedAt,
            session.CompletedAt,
            session.ScorePercent,
            items,
            session.GradingStatus.ToString(),
            session.AiOverallFeedback,
            DeserializeStringList(session.AiWeakTopicsJson),
            DeserializeStringList(session.AiStrengthsJson));
    }

    /// <summary>
    ///     Maps an item with the session's reveal gate already resolved. <paramref name="revealAnswered"/>=false
    ///     means a grade-at-end session still IN_PROGRESS — answered items keep their correct answers hidden.
    /// </summary>
    public static SessionItemDto ToItemDto(
        TrainingSessionItem item,
        bool revealAnswered = true,
        bool hasPro = true)
    {
        IReadOnlyList<SessionOptionDto> options = DeserializeOptions(item.OptionsJson);
        bool isAnswered = item.AnsweredAt is not null;

        // Монетизация по типу вопроса (#623): OPEN_TEXT (развёрнутый/голосовой ответ → AI-анализ) —
        // только PRO. Закрытые вопросы (choice/exact-text, авто-грейд без AI) никогда не locked.
        // hasPro=true для admin/PRO (резолв в хендлере), поэтому им замок не показывается.
        // Closed-вопрос здесь НЕ гейтится по IsFreeSample осознанно (#674, code-review SF-2): не-PRO
        // сессия никогда не содержит не-free closed-вопрос (старт фильтрует до IsFreeSample), а если PRO
        // истёк посреди уже стартованной PRO-сессии — закрытые вопросы, легитимно выданные при старте,
        // остаются видны на resume (принятый trade-off: контент уже доставлен под PRO; снапшот не хранит IsFreeSample).
        bool isOpenText = string.Equals(item.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal);
        bool isLocked = isOpenText && !hasPro;

        // Reveal-gate: ключ грейдинга раскрывается только для ОТВЕЧЕННОГО item'а И только если
        // gate открыт (PER_QUESTION, либо после Complete). Для grade-at-end-теста, пока сессия
        // IN_PROGRESS, у отвеченного item'а скрываем НЕ только ключ, но и вердикт/балл — иначе
        // «вслепую» теряет смысл. Свой ответ (AnswerRaw) виден всегда. У НЕотвеченного item'а
        // вердикт остаётся стартовым PENDING (это статус «ещё не оценён», не утечка ответа).
        bool revealKey = isAnswered && revealAnswered;
        bool revealVerdict = !isAnswered || revealAnswered;

        IReadOnlyList<Guid>? correctOptionIds = null;
        string? referenceAnswer = null;
        string? explanation = null;

        if (revealKey && item.GradingKeyJson is not null)
        {
            GradingKey? key = JsonSerializer.Deserialize<GradingKey>(item.GradingKeyJson, JsonOptions);
            if (key is not null)
            {
                correctOptionIds = key.CorrectOptionIds;
                referenceAnswer = key.ReferenceAnswer;
                explanation = key.Explanation;
            }
        }

        // Server-side redaction (#674): when locked for a non-PRO caller, RedactLocked blanks
        // text/options/key/reference/explanation/feedback so no content leaks — only safe metadata
        // (ids, type, difficulty, section, IsLocked, LockReason) survives.
        return LockedContentRedactor.RedactLocked(new SessionItemDto(
            item.Id,
            item.QuestionId,
            item.TopicId,
            item.QuestionType,
            item.QuestionText,
            options,
            item.Section,
            item.Difficulty,
            item.SortIndex,
            isAnswered,
            item.AnswerRaw,
            revealVerdict ? item.ScorePercent : null,
            revealVerdict ? item.Verdict?.ToString() : null,
            // AI feedback is part of the grading result — gate it with verdict/score so a grade-at-end
            // session still IN_PROGRESS doesn't surface it (it's also only filled post-Complete, #585).
            revealVerdict ? item.Feedback : null,
            correctOptionIds,
            referenceAnswer,
            explanation,
            isLocked,
            isLocked ? TrainerProAccessPolicy.LOCK_REASON_PRO_REQUIRED : null));
    }

    public static IReadOnlyList<SessionOptionDto> DeserializeOptions(string optionsJson) =>
        JsonSerializer.Deserialize<List<SessionOptionDto>>(optionsJson, JsonOptions) ?? [];

    /// <summary>Deserializes a JSON string-array column (AI weak-topics / strengths) into a list; null/blank → empty.</summary>
    private static IReadOnlyList<string> DeserializeStringList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
}
