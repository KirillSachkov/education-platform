using System.Text.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Features.Sessions;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Grading;

/// <summary>
///     Shared recording path for an OPEN_TEXT answer in a training session — used by both
///     <c>CheckAnswerHandler</c> (typed answer) and <c>SubmitVoiceAnswerHandler</c> (transcribed
///     spoken answer, #585). OPEN_TEXT is never auto-graded: the verdict is <c>PENDING</c>, the
///     score is <c>null</c>, mastery/study-state are untouched, and nothing of the grading key is
///     revealed. The reveal-gate mirrors <see cref="CheckAnswerResponse"/> exactly (END_OF_SESSION
///     hides the verdict/key until Complete; PER_QUESTION would reveal — but an OPEN_TEXT verdict is
///     always PENDING with a null key regardless, so both branches return PENDING here).
/// </summary>
public static class OpenAnswerRecorder
{
    /// <summary>
    ///     Records <paramref name="answerRaw"/> as the OPEN_TEXT answer of <paramref name="item"/>
    ///     (verdict PENDING, no score, no key revealed) and returns the gated response. Caller still
    ///     owns the <c>SaveChanges</c> — the mutation is staged on the tracked aggregate.
    /// </summary>
    public static Result<CheckAnswerResponse, Error> Record(
        TrainingSession session,
        TrainingSessionItem item,
        string? answerRaw)
    {
        GradingKey key = item.GradingKeyJson is null
            ? new GradingKey([], null, null)
            : JsonSerializer.Deserialize<GradingKey>(item.GradingKeyJson, SessionMapper.JsonOptions)
                ?? new GradingKey([], null, null);

        // OPEN_TEXT → PENDING / null score (AnswerGrader contract). Use the grader rather than a
        // hardcoded PENDING so the verdict stays in lockstep with CheckAnswer's open-answer path.
        GradeOutcome outcome = AnswerGrader.Grade(item.QuestionType, key, null, answerRaw);

        UnitResult<Error> recordResult = session.RecordAnswer(
            item.Id,
            answerRaw,
            outcome.ScorePercent,
            outcome.Verdict,
            feedback: null);
        if (recordResult.IsFailure)
            return recordResult.Error;

        // Reveal-gate: PER_QUESTION reveals the key, END_OF_SESSION hides it until Complete. An
        // OPEN_TEXT answer carries no auto-graded key, so PENDING / null is returned either way.
        bool reveal = session.RevealPolicy == RevealPolicy.PER_QUESTION;

        return reveal
            ? new CheckAnswerResponse(
                item.Id,
                outcome.Verdict.ToString(),
                outcome.ScorePercent,
                key.CorrectOptionIds,
                key.ReferenceAnswer,
                key.Explanation)
            : new CheckAnswerResponse(
                item.Id,
                AnswerVerdict.PENDING.ToString(),
                ScorePercent: null,
                CorrectOptionIds: null,
                ReferenceAnswer: null,
                Explanation: null);
    }
}
