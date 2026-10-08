using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrainerService.Domain;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;
using TrainerService.Web.Configuration;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     <c>recompute-mastery</c> backfill CLI (#691): re-derives every <c>topic_masteries</c> row from
///     session-item history as the difficulty-weighted average of the latest score per UNIQUE question —
///     fixing rows inflated by the old EWMA. Drives the CLI core (<see cref="RecomputeMasteryCli.RecomputeAllAsync"/>)
///     against seeded data and asserts the persisted result.
/// </summary>
public sealed class RecomputeMasteryCliTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Recompute_rewrites_an_inflated_row_from_distinct_question_latest_weighted_history()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();
        Guid seniorQ = Guid.NewGuid();
        Guid juniorQ = Guid.NewGuid();

        // Inflated row as the old farm-prone EWMA would have left it (95% over 10 "answers").
        await SeedMasteryAsync(userId, topicId, masteryPercent: 95, answersCount: 10);

        // History: SENIOR question answered correct (100). JUNIOR question answered TWICE — first 100,
        // then later 0 — to prove the backfill takes the LATEST attempt (0) and counts the question once.
        await SeedAnsweredSessionAsync(userId, topicId, (seniorQ, "SENIOR", 100), backdateDays: 0);
        await SeedAnsweredSessionAsync(userId, topicId, (juniorQ, "JUNIOR", 100), backdateDays: 5);
        await SeedAnsweredSessionAsync(userId, topicId, (juniorQ, "JUNIOR", 0), backdateDays: 0);

        await RunRecomputeAsync();

        // Distinct questions: SENIOR latest 100, JUNIOR latest 0 → round((1.3*100 + 0.7*0)/2.0) = 65, count 2.
        (int mastery, int answers) = await GetMasteryAsync(userId, topicId);
        Assert.Equal(65, mastery);
        Assert.Equal(2, answers);
    }

    [Fact]
    public async Task Recompute_resets_a_row_that_has_no_scored_history()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();

        // Inflated orphan: a mastery row with no backing answers at all.
        await SeedMasteryAsync(userId, topicId, masteryPercent: 88, answersCount: 7);

        await RunRecomputeAsync();

        (int mastery, int answers) = await GetMasteryAsync(userId, topicId);
        Assert.Equal(0, mastery);
        Assert.Equal(0, answers);
    }

    // --- helpers ---

    private async Task RunRecomputeAsync()
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        await RecomputeMasteryCli.RecomputeAllAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private Task SeedMasteryAsync(Guid userId, Guid topicId, int masteryPercent, int answersCount) =>
        ExecuteInDbAsync(async db =>
        {
            TopicMastery mastery = TopicMastery.Create(userId, topicId);
            mastery.SetDerived(masteryPercent, answersCount, DateTime.UtcNow);
            await db.TopicMasteries.AddAsync(mastery);
            return await db.SaveChangesAsync();
        });

    /// <summary>
    ///     Seeds one COMPLETED session with a single answered question. <paramref name="backdateDays"/>
    ///     shifts <c>answered_at</c> into the past so "latest attempt" is deterministic across re-answers.
    /// </summary>
    private async Task SeedAnsweredSessionAsync(
        Guid userId, Guid topicId, (Guid QuestionId, string Difficulty, int Score) answer, int backdateDays)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId]).Value;
            session.AddItem(answer.QuestionId, topicId, "SINGLE_CHOICE", "Q", "[]", null, answer.Difficulty, 0, null);
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            AnswerVerdict verdict = answer.Score >= 50 ? AnswerVerdict.CORRECT : AnswerVerdict.INCORRECT;
            session.RecordAnswer(session.Items[0].Id, "x", answer.Score, verdict, null);
            session.Complete(answer.Score);
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

    private Task<(int Mastery, int Answers)> GetMasteryAsync(Guid userId, Guid topicId) =>
        ExecuteInDbAsync(async db =>
        {
            TopicMastery row = await db.TopicMasteries
                .AsNoTracking()
                .SingleAsync(m => m.UserId == userId && m.TopicId == topicId);
            return (row.MasteryPercent, row.AnswersCount);
        });
}
