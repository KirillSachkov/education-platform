using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Stats;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/stats/mock-trend (#568): completed-MOCK score trend (ascending by date) + aggregated
///     weak/strong topics from the AI feedback JSON (dedup, cap, newest-first). Sessions seeded in the DB.
/// </summary>
public sealed class MockTrendTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task MockTrend_is_empty_when_the_user_has_no_mocks()
    {
        AuthenticateAs("platform-participant");

        TrainerMockTrendDto trend = await ReadResultAsync<TrainerMockTrendDto>(
            await Client.GetAsync("/trainer/stats/mock-trend"));

        Assert.Empty(trend.Attempts);
        Assert.Empty(trend.WeakTopics);
        Assert.Empty(trend.StrongTopics);
    }

    [Fact]
    public async Task MockTrend_returns_attempts_ascending_and_aggregates_topics_newest_first()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        // Older mock scored 40 with one weak topic; newer mock scored 80 with a different weak topic.
        Guid older = await SeedCompletedMockAsync(
            userId, topicId, score: 40, completedAt: now.AddDays(-2),
            weakTopics: ["Память"], strengths: ["Синтаксис"]);
        Guid newer = await SeedCompletedMockAsync(
            userId, topicId, score: 80, completedAt: now.AddDays(-1),
            weakTopics: ["Многопоточность", "Память"], strengths: ["LINQ", "Синтаксис"]);

        // A drill (non-MOCK) and an in-progress mock must NOT appear in the trend.
        await SeedCompletedDrillAsync(userId, topicId, score: 99, completedAt: now);

        AuthenticateAs("platform-participant", userId);
        TrainerMockTrendDto trend = await ReadResultAsync<TrainerMockTrendDto>(
            await Client.GetAsync("/trainer/stats/mock-trend"));

        // Only the two completed mocks, ascending by completion date.
        Assert.Equal(2, trend.Attempts.Count);
        Assert.Equal(older, trend.Attempts[0].SessionId);
        Assert.Equal(40, trend.Attempts[0].ScorePercent);
        Assert.Equal(newer, trend.Attempts[1].SessionId);
        Assert.Equal(80, trend.Attempts[1].ScorePercent);

        // Topics aggregated newest-first, deduped: newer mock's topics come first; "Память" appears once.
        Assert.Equal(["Многопоточность", "Память"], trend.WeakTopics);
        Assert.Equal(["LINQ", "Синтаксис"], trend.StrongTopics);
    }

    // --- helpers ---

    private Task<Guid> SeedCompletedMockAsync(
        Guid userId,
        Guid topicId,
        int score,
        DateTime completedAt,
        string[] weakTopics,
        string[] strengths) =>
        SeedCompletedSessionAsync(userId, topicId, TrainingMode.MOCK, score, completedAt, weakTopics, strengths);

    private Task<Guid> SeedCompletedDrillAsync(Guid userId, Guid topicId, int score, DateTime completedAt) =>
        SeedCompletedSessionAsync(userId, topicId, TrainingMode.DRILL, score, completedAt, null, null);

    private async Task<Guid> SeedCompletedSessionAsync(
        Guid userId,
        Guid topicId,
        TrainingMode mode,
        int score,
        DateTime completedAt,
        string[]? weakTopics,
        string[]? strengths)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, mode, trackId: null, [topicId]).Value;
            session.AddItem(
                questionId: Guid.NewGuid(),
                topicId: topicId,
                questionType: "SINGLE_CHOICE",
                questionText: "Q",
                optionsJson: "[]",
                section: null,
                difficulty: "MIDDLE",
                sortIndex: 0,
                gradingKeyJson: null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync(); // assigns the item id via the value generator

            session.RecordAnswer(session.Items[0].Id, "x", score, AnswerVerdict.CORRECT, null);
            session.Complete(score);

            if (weakTopics is not null || strengths is not null)
                session.RecordAiOverall(
                    "overall",
                    weakTopics is null ? null : JsonSerializer.Serialize(weakTopics),
                    strengths is null ? null : JsonSerializer.Serialize(strengths));

            await db.SaveChangesAsync();
            return session.Id;
        });

        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_sessions SET completed_at = {completedAt} WHERE id = {sessionId}"));

        return sessionId;
    }
}
