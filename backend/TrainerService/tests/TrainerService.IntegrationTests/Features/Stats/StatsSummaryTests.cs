using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Stats;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/stats/summary (#568): all-time answered/accuracy, study-status breakdown, studied
///     count, per-difficulty accuracy (J/M/S always present), SRS forecast (due today + 7-day + retention).
/// </summary>
public sealed class StatsSummaryTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Summary_is_all_zeros_for_a_fresh_profile()
    {
        AuthenticateAs("platform-participant");

        TrainerStatsSummaryDto summary = await ReadResultAsync<TrainerStatsSummaryDto>(
            await Client.GetAsync("/trainer/stats/summary"));

        Assert.Equal(0, summary.TotalAnswered);
        Assert.Equal(0, summary.AllTimeAccuracyPercent);
        Assert.Empty(summary.StudyStatusBreakdown);
        Assert.Equal(0, summary.StudiedQuestions);

        // The three difficulty buckets are always present, zeroed.
        Assert.Equal(3, summary.DifficultyAccuracy.Count);
        Assert.Equal(["JUNIOR", "MIDDLE", "SENIOR"], summary.DifficultyAccuracy.Select(d => d.Difficulty));
        Assert.All(summary.DifficultyAccuracy, d => Assert.Equal(0, d.Answered));

        Assert.Equal(0, summary.Srs.DueToday);
        Assert.Empty(summary.Srs.Upcoming);
        Assert.Equal(0, summary.Srs.RetentionPercent);
    }

    [Fact]
    public async Task Summary_aggregates_answers_difficulty_study_states_and_srs()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();

        // Session items: JUNIOR correct, JUNIOR wrong, MIDDLE correct, SENIOR OPEN (no score → not counted).
        await SeedSessionAsync(userId, topicId,
            ("JUNIOR", 100, AnswerVerdict.CORRECT),
            ("JUNIOR", 0, AnswerVerdict.INCORRECT),
            ("MIDDLE", 100, AnswerVerdict.CORRECT),
            ("SENIOR", null, AnswerVerdict.PENDING));

        // Study-states: 2 KNOWN (TimesSeen=2,Known=2 and 3/2), 1 WRONG (TimesSeen=1,Known=0). One KNOWN due today.
        await SeedStudyStateAsync(userId, topicId, StudyStatus.KNOWN, timesSeen: 2, timesKnown: 2, nextDueOffsetDays: -1);
        await SeedStudyStateAsync(userId, topicId, StudyStatus.KNOWN, timesSeen: 3, timesKnown: 2, nextDueOffsetDays: 3);
        await SeedStudyStateAsync(userId, topicId, StudyStatus.WRONG, timesSeen: 1, timesKnown: 0, nextDueOffsetDays: 1);

        AuthenticateAs("platform-participant", userId);
        TrainerStatsSummaryDto summary = await ReadResultAsync<TrainerStatsSummaryDto>(
            await Client.GetAsync("/trainer/stats/summary"));

        // Answered = 3 auto-graded (SENIOR OPEN excluded); 2 correct → 67%.
        Assert.Equal(3, summary.TotalAnswered);
        Assert.Equal(67, summary.AllTimeAccuracyPercent);

        // Per-difficulty: JUNIOR 1/2, MIDDLE 1/1, SENIOR 0/0 (OPEN not auto-graded).
        DifficultyAccuracyDto junior = Assert.Single(summary.DifficultyAccuracy, d => d.Difficulty == "JUNIOR");
        Assert.Equal(2, junior.Answered);
        Assert.Equal(1, junior.Correct);
        Assert.Equal(50, junior.AccuracyPercent);

        DifficultyAccuracyDto middle = Assert.Single(summary.DifficultyAccuracy, d => d.Difficulty == "MIDDLE");
        Assert.Equal(1, middle.Answered);
        Assert.Equal(1, middle.Correct);
        Assert.Equal(100, middle.AccuracyPercent);

        DifficultyAccuracyDto senior = Assert.Single(summary.DifficultyAccuracy, d => d.Difficulty == "SENIOR");
        Assert.Equal(0, senior.Answered);

        // Study-status breakdown: 2 KNOWN + 1 WRONG; studied = 3.
        Assert.Equal(3, summary.StudiedQuestions);
        StudyStatusCountDto known = Assert.Single(summary.StudyStatusBreakdown, s => s.Status == "KNOWN");
        Assert.Equal(2, known.Count);
        StudyStatusCountDto wrong = Assert.Single(summary.StudyStatusBreakdown, s => s.Status == "WRONG");
        Assert.Equal(1, wrong.Count);

        // SRS: one row due today (offset -1); the +3-day row lands in the 7-day upcoming forecast (the
        // WRONG row at +1 day also lands there → 2 upcoming days, each with 1 due question).
        Assert.Equal(1, summary.Srs.DueToday);
        Assert.Equal(2, summary.Srs.Upcoming.Count);
        Assert.All(summary.Srs.Upcoming, u => Assert.Equal(1, u.Due));

        // Retention = sum(known)/sum(seen) = (2+2+0)/(2+3+1) = 4/6 = 67%.
        Assert.Equal(67, summary.Srs.RetentionPercent);
    }

    [Fact]
    public async Task Difficulty_accuracy_counts_distinct_questions_by_latest_attempt()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();

        // The SAME MIDDLE question answered twice: first wrong (older), then correct (latest, #691).
        await SeedSingleAnswerSessionAsync(userId, topicId, questionId, "MIDDLE", 0, AnswerVerdict.INCORRECT, backdateDays: 5);
        await SeedSingleAnswerSessionAsync(userId, topicId, questionId, "MIDDLE", 100, AnswerVerdict.CORRECT, backdateDays: 0);

        AuthenticateAs("platform-participant", userId);
        TrainerStatsSummaryDto summary = await ReadResultAsync<TrainerStatsSummaryDto>(
            await Client.GetAsync("/trainer/stats/summary"));

        // The re-answered question counts ONCE; the latest attempt (CORRECT) drives answered + correct.
        Assert.Equal(1, summary.TotalAnswered);
        Assert.Equal(100, summary.AllTimeAccuracyPercent);
        DifficultyAccuracyDto middle = Assert.Single(summary.DifficultyAccuracy, d => d.Difficulty == "MIDDLE");
        Assert.Equal(1, middle.Answered);
        Assert.Equal(1, middle.Correct);
        Assert.Equal(100, middle.AccuracyPercent);
    }

    // --- helpers ---

    /// <summary>Seeds one COMPLETED session with a single answered question, optionally backdating its
    /// <c>answered_at</c> so "latest attempt" ordering across re-answers is deterministic.</summary>
    private async Task SeedSingleAnswerSessionAsync(
        Guid userId, Guid topicId, Guid questionId, string difficulty, int score, AnswerVerdict verdict, int backdateDays)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;
            session.AddItem(questionId, topicId, "SINGLE_CHOICE", "Q", "[]", null, difficulty, 0, null);
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            session.RecordAnswer(session.Items[0].Id, "x", score, verdict, null);
            session.Complete(score);
            await db.SaveChangesAsync();
            return session.Id;
        });

        if (backdateDays > 0)
        {
            DateTime past = DateTime.UtcNow.AddDays(-backdateDays);
            await ExecuteInDbAsync(db => db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_session_items SET answered_at = {past} WHERE session_id = {sessionId}"));
        }
    }

    private async Task SeedSessionAsync(
        Guid userId,
        Guid topicId,
        params (string Difficulty, int? Score, AnswerVerdict Verdict)[] items)
    {
        await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;

            for (int i = 0; i < items.Length; i++)
                session.AddItem(
                    questionId: Guid.NewGuid(),
                    topicId: topicId,
                    questionType: items[i].Difficulty == "SENIOR" ? "OPEN_TEXT" : "SINGLE_CHOICE",
                    questionText: $"Q{i}",
                    optionsJson: "[]",
                    section: null,
                    difficulty: items[i].Difficulty,
                    sortIndex: i,
                    gradingKeyJson: null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            var persisted = session.Items.OrderBy(it => it.SortIndex).ToList();
            for (int i = 0; i < items.Length; i++)
                session.RecordAnswer(persisted[i].Id, "x", items[i].Score, items[i].Verdict, null);

            session.Complete(50);
            return await db.SaveChangesAsync();
        });
    }

    private async Task SeedStudyStateAsync(
        Guid userId,
        Guid topicId,
        StudyStatus status,
        int timesSeen,
        int timesKnown,
        int nextDueOffsetDays)
    {
        Guid questionId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            QuestionStudyState state = QuestionStudyState.Create(userId, questionId, topicId);
            // Drive counters/status through the public grade API (TimesSeen++ per call).
            DateTimeOffset now = DateTimeOffset.UtcNow;
            for (int i = 0; i < timesKnown; i++)
                state.RecordTestResult(correct: true, now);
            for (int i = 0; i < timesSeen - timesKnown; i++)
                state.RecordTestResult(correct: false, now);

            db.QuestionStudyStates.Add(state);
            return await db.SaveChangesAsync();
        });

        // Patch status + next_due_at deterministically (SM-2 would otherwise schedule its own dates).
        DateTimeOffset due = DateTimeOffset.UtcNow.AddDays(nextDueOffsetDays);
        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.question_study_states SET status = {status.ToString()}, next_due_at = {due} WHERE user_id = {userId} AND question_id = {questionId}"));
    }
}
