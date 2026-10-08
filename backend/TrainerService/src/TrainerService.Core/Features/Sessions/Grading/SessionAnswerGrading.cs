using System.Text.Json;
using Shared.AI;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>Inline open-answer grade outcome + the billable LLM usage/model for the caller's ledger.</summary>
public readonly record struct InlineOpenGradeResult(CheckAnswerResponse Response, AiUsage? Usage, string Model);

/// <summary>
///     Shared recording path for ONE checked answer in a training session — used by both
///     <c>CheckAnswerHandler</c> (typed answer) and <c>SubmitVoiceAnswerHandler</c> (transcribed spoken
///     answer, #585). Encapsulates the identical post-grade flow for every input mode: persist the
///     answer on the item → recompute the topic's derived <see cref="TopicMastery"/> (when scored) → upsert
///     <see cref="QuestionStudyState"/> for formative LEARN/DRILL (when scored) → build the
///     reveal-gated <see cref="CheckAnswerResponse"/> (PER_QUESTION reveals key + «Твой ответ»;
///     END_OF_SESSION returns PENDING). Two entry points:
///     <list type="bullet">
///         <item><see cref="RecordAutoGradedAsync"/> — choice / EXACT_TEXT, deterministic grade.</item>
///         <item><see cref="GradeAndRecordOpenInlineAsync"/> — OPEN_TEXT, inline AI grade (fail-soft →
///             PENDING on AI outage), shared verbatim by the typed and the voice path so a spoken
///             open answer in a non-MOCK session is graded exactly like a typed one.</item>
///     </list>
///     The quota / rate-limit / PRO gate stays with the caller — it differs by input dimension
///     (OPEN_GRADE for typed, VOICE for voice) and is not this service's concern.
/// </summary>
public sealed class SessionAnswerGrading
{
    /// <summary>Inline AI grading is bounded — an AI outage must not hang the check request.</summary>
    private static readonly TimeSpan INLINE_GRADE_TIMEOUT = TimeSpan.FromSeconds(30);

    private readonly IOpenAnswerGrader _openAnswerGrader;
    private readonly ITopicMasteryRepository _mastery;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ILogger<SessionAnswerGrading> _logger;

    public SessionAnswerGrading(
        IOpenAnswerGrader openAnswerGrader,
        ITopicMasteryRepository mastery,
        ITrainingSessionsRepository sessions,
        IQuestionStudyStatesRepository studyStates,
        ILogger<SessionAnswerGrading> logger)
    {
        _openAnswerGrader = openAnswerGrader;
        _mastery = mastery;
        _sessions = sessions;
        _studyStates = studyStates;
        _logger = logger;
    }

    /// <summary>
    ///     Records a deterministically auto-graded answer (choice / EXACT_TEXT). Caller still owns the
    ///     <c>SaveChanges</c> — the mutation is staged on the tracked aggregate.
    /// </summary>
    public async Task<Result<CheckAnswerResponse, Error>> RecordAutoGradedAsync(
        TrainingSession session,
        TrainingSessionItem item,
        Guid userId,
        IReadOnlyList<Guid>? optionIds,
        string? text,
        CancellationToken ct)
    {
        GradingKey key = ParseKey(item);
        GradeOutcome outcome = AnswerGrader.Grade(item.QuestionType, key, optionIds, text);
        string? answerRaw = BuildAnswerRaw(optionIds, text);

        // Choice/exact answers never need «Твой ответ» echoed back — the client owns its own draft.
        return await RecordAsync(session, item, userId, key, answerRaw, outcome, feedback: null, revealAnswerText: false, ct);
    }

    /// <summary>
    ///     Inline AI grade of one OPEN_TEXT answer in a non-MOCK session, then records it (+ mastery +
    ///     study-state) and returns the gated response plus the billable LLM usage/model. Fail-soft: on
    ///     any AI failure/timeout/throw the answer stays <c>PENDING</c> with a null score (mastery /
    ///     study-state untouched, retryable) and the response carries a short user-facing notice — the
    ///     check never 500s. Caller owns the <c>SaveChanges</c> and the AI-usage ledger entry.
    /// </summary>
    public async Task<Result<InlineOpenGradeResult, Error>> GradeAndRecordOpenInlineAsync(
        TrainingSession session,
        TrainingSessionItem item,
        Guid userId,
        string? studentText,
        CancellationToken ct)
    {
        GradingKey key = ParseKey(item);
        (GradeOutcome outcome, string? feedback, AiUsage? usage, string model) =
            await GradeOpenInlineAsync(item.QuestionText, key.ReferenceAnswer, studentText, ct);

        string? answerRaw = BuildAnswerRaw(null, studentText);

        Result<CheckAnswerResponse, Error> recorded = await RecordAsync(
            session, item, userId, key, answerRaw, outcome, feedback, revealAnswerText: true, ct);
        if (recorded.IsFailure)
            return recorded.Error;

        return new InlineOpenGradeResult(recorded.Value, usage, model);
    }

    /// <summary>
    ///     Persists the answer + mastery + study-state and returns the reveal-gated response. PER_QUESTION
    ///     reveals the key (+ «Твой ответ» when <paramref name="revealAnswerText"/>); END_OF_SESSION hides
    ///     everything as PENDING until Complete. Derived mastery + study-state are updated only when the
    ///     answer carries a real score (auto-graded always; OPEN_TEXT only when the AI grade succeeded).
    /// </summary>
    private async Task<Result<CheckAnswerResponse, Error>> RecordAsync(
        TrainingSession session,
        TrainingSessionItem item,
        Guid userId,
        GradingKey key,
        string? answerRaw,
        GradeOutcome outcome,
        string? feedback,
        bool revealAnswerText,
        CancellationToken ct)
    {
        UnitResult<Error> recordResult = session.RecordAnswer(
            item.Id,
            answerRaw,
            outcome.ScorePercent,
            outcome.Verdict,
            feedback);
        if (recordResult.IsFailure)
            return recordResult.Error;

        // Derived mastery + study-state move only for scored answers. Тему берём С ITEM'а (multi-topic
        // MOCK-сессия → поднять mastery именно той темы, из которой пришёл вопрос).
        if (outcome.ScorePercent is int score)
        {
            UnitResult<Error> masteryResult = await UpdateMasteryAsync(userId, item, score, ct);
            if (masteryResult.IsFailure)
                return masteryResult.Error;

            // Formative LEARN/DRILL feed the question study-state (KNOWN/WRONG + SRS) so the answer
            // двигает «изучено» темы и кладёт ошибки в «Повтор»/«Мои ошибки». MOCK не трогает study-state.
            // «Засчитано» = вердикт CORRECT, а НЕ score == 100. У авто-грейда choice/exact это эквивалентно
            // (CORRECT ⇔ 100), но AI-грейд открытого ответа ставит вердикт CORRECT с НЕ-100 баллом (балл —
            // мера полноты): при `score == 100` любой «Верно» открытого ответа уезжал в WRONG и тема «не
            // засчитывалась». Вердикт — это то, что видит пользователь, поэтому он и питает study-state.
            if (session.Mode is TrainingMode.LEARN or TrainingMode.DRILL)
            {
                UnitResult<Error> studyResult = await RecordStudyStateAsync(
                    userId, item.QuestionId, item.TopicId, correct: outcome.Verdict == AnswerVerdict.CORRECT, ct);
                if (studyResult.IsFailure)
                    return studyResult.Error;
            }
        }

        bool reveal = session.RevealPolicy == RevealPolicy.PER_QUESTION;
        return reveal
            ? new CheckAnswerResponse(
                item.Id,
                outcome.Verdict.ToString(),
                outcome.ScorePercent,
                key.CorrectOptionIds,
                key.ReferenceAnswer,
                key.Explanation,
                feedback,
                revealAnswerText ? answerRaw : null)
            : new CheckAnswerResponse(
                item.Id,
                AnswerVerdict.PENDING.ToString(),
                ScorePercent: null,
                CorrectOptionIds: null,
                ReferenceAnswer: null,
                Explanation: null,
                Feedback: null,
                AnswerText: null);
    }

    /// <summary>
    ///     Inline AI grade of one open answer. Bounded by <see cref="INLINE_GRADE_TIMEOUT"/> and fully
    ///     fail-soft: on any AI failure, timeout, or thrown exception → <c>PENDING</c>, null score, short
    ///     notice (mastery/study-state stay untouched, the student can retry). An AI outage never 500s.
    /// </summary>
    private async Task<(GradeOutcome Outcome, string? Feedback, AiUsage? Usage, string Model)> GradeOpenInlineAsync(
        string questionStem,
        string? referenceAnswer,
        string? studentText,
        CancellationToken ct)
    {
        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(INLINE_GRADE_TIMEOUT);
            Result<OpenAnswerGrade, Error> grade =
                await _openAnswerGrader.GradeAsync(questionStem, referenceAnswer, studentText, cts.Token);

            if (grade.IsSuccess)
            {
                return (
                    new GradeOutcome(grade.Value.Verdict, grade.Value.ScorePercent),
                    grade.Value.Feedback,
                    grade.Value.Usage,
                    grade.Value.Model);
            }

            _logger.LogWarning(
                "Inline open-answer grading failed ({Code}); leaving PENDING (retryable).",
                grade.Error.Messages[0].Code);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Inline open-answer grading timed out; leaving PENDING (retryable).");
        }
#pragma warning disable CA1031 // an AI outage must never fail the check — degrade to a retryable PENDING
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Inline open-answer grading threw; leaving PENDING (retryable).");
        }

        return (new GradeOutcome(AnswerVerdict.PENDING, null), "Проверка ответа временно недоступна", null, string.Empty);
    }

    /// <summary>
    ///     Recomputes the topic's DERIVED mastery (#691) — the difficulty-weighted average of the latest
    ///     score per UNIQUE question — and stores it on the aggregate. History is read from the DB (which
    ///     excludes the answer just staged on the tracked session, not yet saved), so the freshly graded
    ///     answer is merged in: it is by definition the latest attempt for its question and overrides any
    ///     stored attempt, so re-answering the same question never inflates mastery or the answer count.
    /// </summary>
    private async Task<UnitResult<Error>> UpdateMasteryAsync(
        Guid userId, TrainingSessionItem item, int score, CancellationToken ct)
    {
        Guid topicId = item.TopicId;

        IReadOnlyList<QuestionLatestScore> history =
            await _sessions.GetLatestScoredAnswersPerQuestionAsync(userId, topicId, ct);

        Dictionary<Guid, QuestionLatestScore> byQuestion = history.ToDictionary(q => q.QuestionId);
        byQuestion[item.QuestionId] = new QuestionLatestScore(item.QuestionId, score, item.Difficulty);

        (int masteryPercent, int answersCount) = MasteryCalculator.Compute(byQuestion.Values.ToList());

        Result<TopicMastery, Error> existing = await _mastery.GetByAsync(
            m => m.UserId == userId && m.TopicId == topicId,
            ct);

        TopicMastery mastery;
        if (existing.IsSuccess)
        {
            mastery = existing.Value;
        }
        else
        {
            mastery = TopicMastery.Create(userId, topicId);
            await _mastery.AddAsync(mastery, ct);
        }

        return mastery.SetDerived(masteryPercent, answersCount, DateTime.UtcNow);
    }

    private async Task<UnitResult<Error>> RecordStudyStateAsync(
        Guid userId,
        Guid questionId,
        Guid topicId,
        bool correct,
        CancellationToken ct)
    {
        Result<QuestionStudyState, Error> existing = await _studyStates.GetByAsync(userId, questionId, ct);

        QuestionStudyState state;
        if (existing.IsSuccess)
        {
            state = existing.Value;
        }
        else
        {
            state = QuestionStudyState.Create(userId, questionId, topicId);
            await _studyStates.AddAsync(state, ct);
        }

        state.RecordTestResult(correct, DateTimeOffset.UtcNow);
        return UnitResult.Success<Error>();
    }

    private static GradingKey ParseKey(TrainingSessionItem item) =>
        item.GradingKeyJson is null
            ? new GradingKey([], null, null)
            : JsonSerializer.Deserialize<GradingKey>(item.GradingKeyJson, SessionMapper.JsonOptions)
                ?? new GradingKey([], null, null);

    private static string? BuildAnswerRaw(IReadOnlyList<Guid>? optionIds, string? text)
    {
        if (optionIds is { Count: > 0 })
            return string.Join(",", optionIds);

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
