using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Stats;
using TrainerService.Domain;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.Topics;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/stats/strengths (#614 H): strong/weak topics grounded in MEASURED per-topic mastery
///     across all activity (not the single-mock AI feedback), with the MIN_ATTEMPTS exclusion, sample-size
///     context, and the &gt;=2-mock de-noised AI hint supplement. All data is seeded directly in the DB.
/// </summary>
public sealed class StrengthsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Strengths_is_empty_for_a_user_with_no_mastery()
    {
        AuthenticateAs("platform-participant");

        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        Assert.Empty(dto.StrongTopics);
        Assert.Empty(dto.WeakTopics);
        Assert.Empty(dto.MockHintTopics);
        Assert.Equal(0, dto.SampleSize.AssessedTopics);
        Assert.Equal(0, dto.SampleSize.TotalAnswers);
        Assert.Equal(0, dto.SampleSize.SessionsCount);
        Assert.Equal(0, dto.SampleSize.MockCount);
    }

    [Fact]
    public async Task Strengths_ranks_topics_by_measured_mastery_above_and_below_threshold()
    {
        Guid userId = Guid.NewGuid();
        Guid trackId = await SeedTrackAsync();

        // Strong topic (90, 4 answers), weak topic (30, 5 answers). Both clear the min-attempts gate.
        Guid strongTopic = await SeedTopicAsync(trackId, "csharp-strong", "C# силён");
        Guid weakTopic = await SeedTopicAsync(trackId, "async-weak", "Асинхронность");
        await SeedMasteryAsync(userId, strongTopic, masteryPercent: 90, answersCount: 4);
        await SeedMasteryAsync(userId, weakTopic, masteryPercent: 30, answersCount: 5);
        // #691 strong-gate: the strong topic needs a MIDDLE/SENIOR answered question to be crowned.
        await SeedAnsweredQuestionAsync(userId, strongTopic, "MIDDLE");

        AuthenticateAs("platform-participant", userId);
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        StrengthTopicDto strong = Assert.Single(dto.StrongTopics);
        Assert.Equal(strongTopic, strong.TopicId);
        Assert.Equal("C# силён", strong.Title);
        Assert.Equal(90, strong.MasteryPercent);
        Assert.Equal(4, strong.Attempts);

        StrengthTopicDto weak = Assert.Single(dto.WeakTopics);
        Assert.Equal(weakTopic, weak.TopicId);
        Assert.Equal("Асинхронность", weak.Title);
        Assert.Equal(30, weak.MasteryPercent);

        Assert.Equal(2, dto.SampleSize.AssessedTopics);
        Assert.Equal(9, dto.SampleSize.TotalAnswers); // 4 + 5
    }

    [Fact]
    public async Task Strong_topic_needs_a_middle_or_senior_answered_question()
    {
        Guid userId = Guid.NewGuid();
        Guid trackId = await SeedTrackAsync();

        // Both topics have high, well-sampled mastery (>= STRONG_THRESHOLD, >= MIN_ATTEMPTS).
        Guid juniorOnly = await SeedTopicAsync(trackId, "junior-only", "Только Junior");
        Guid hasSenior = await SeedTopicAsync(trackId, "has-senior", "Есть Senior");
        await SeedMasteryAsync(userId, juniorOnly, masteryPercent: 95, answersCount: 5);
        await SeedMasteryAsync(userId, hasSenior, masteryPercent: 80, answersCount: 5);

        // The first topic was only ever answered on JUNIOR questions → must NOT be crowned strong (#691).
        await SeedAnsweredQuestionAsync(userId, juniorOnly, "JUNIOR");
        // The second has a SENIOR answered question → qualifies.
        await SeedAnsweredQuestionAsync(userId, hasSenior, "SENIOR");

        AuthenticateAs("platform-participant", userId);
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        StrengthTopicDto strong = Assert.Single(dto.StrongTopics);
        Assert.Equal(hasSenior, strong.TopicId);
        // The Junior-only topic is high-mastery but excluded from the strong column by the gate.
        Assert.DoesNotContain(dto.StrongTopics, t => t.TopicId == juniorOnly);
    }

    [Fact]
    public async Task Strengths_excludes_topics_below_min_attempts()
    {
        Guid userId = Guid.NewGuid();
        Guid trackId = await SeedTrackAsync();

        // High mastery but only 2 answers (< MIN_ATTEMPTS=3) → must NOT appear in either column.
        Guid lowSampleStrong = await SeedTopicAsync(trackId, "low-strong", "Мало попыток (сильно)");
        await SeedMasteryAsync(userId, lowSampleStrong, masteryPercent: 95, answersCount: 2);
        // Low mastery but only 1 answer → also excluded (this is what stops flooding «подтянуть»).
        Guid lowSampleWeak = await SeedTopicAsync(trackId, "low-weak", "Мало попыток (слабо)");
        await SeedMasteryAsync(userId, lowSampleWeak, masteryPercent: 10, answersCount: 1);
        // A properly-assessed weak topic (3 answers) → appears.
        Guid assessedWeak = await SeedTopicAsync(trackId, "real-weak", "Реальная слабость");
        await SeedMasteryAsync(userId, assessedWeak, masteryPercent: 40, answersCount: 3);

        AuthenticateAs("platform-participant", userId);
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        Assert.Empty(dto.StrongTopics); // the 95% topic had too few attempts
        StrengthTopicDto weak = Assert.Single(dto.WeakTopics);
        Assert.Equal(assessedWeak, weak.TopicId);
        Assert.Equal(1, dto.SampleSize.AssessedTopics); // only the 3-answer topic counts
        Assert.Equal(3, dto.SampleSize.TotalAnswers);
    }

    [Fact]
    public async Task Strengths_reports_session_and_mock_sample_size()
    {
        Guid userId = Guid.NewGuid();
        Guid trackId = await SeedTrackAsync();
        Guid topicId = await SeedTopicAsync(trackId, "sample-topic", "Тема выборки");
        await SeedMasteryAsync(userId, topicId, masteryPercent: 80, answersCount: 6);

        // Two completed mocks + one drill → SessionsCount=3, MockCount=2.
        await SeedCompletedMockAsync(userId, topicId, weakTopics: null, strengths: null);
        await SeedCompletedMockAsync(userId, topicId, weakTopics: null, strengths: null);
        await SeedCompletedDrillAsync(userId, topicId);

        AuthenticateAs("platform-participant", userId);
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        Assert.Equal(3, dto.SampleSize.SessionsCount);
        Assert.Equal(2, dto.SampleSize.MockCount);
        Assert.Equal(1, dto.SampleSize.AssessedTopics);
        Assert.Equal(6, dto.SampleSize.TotalAnswers);
    }

    [Fact]
    public async Task MockHints_only_include_topics_seen_in_at_least_two_mocks()
    {
        Guid userId = Guid.NewGuid();
        Guid trackId = await SeedTrackAsync();
        Guid topicId = await SeedTopicAsync(trackId, "hint-topic", "Тема подсказок");

        // "Память" in two mocks → kept. "Синтаксис" in only one mock → dropped (one-off LLM mention).
        await SeedCompletedMockAsync(userId, topicId, weakTopics: ["Память"], strengths: ["Синтаксис"]);
        await SeedCompletedMockAsync(userId, topicId, weakTopics: ["Память"], strengths: null);

        AuthenticateAs("platform-participant", userId);
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        Assert.Equal(["Память"], dto.MockHintTopics);
    }

    [Fact]
    public async Task Strengths_is_scoped_to_caller()
    {
        Guid trackId = await SeedTrackAsync();
        Guid topicId = await SeedTopicAsync(trackId, "other-topic", "Чужая тема");
        await SeedMasteryAsync(Guid.NewGuid(), topicId, masteryPercent: 90, answersCount: 5);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        TrainerStrengthsDto dto = await ReadResultAsync<TrainerStrengthsDto>(
            await Client.GetAsync("/trainer/stats/strengths"));

        Assert.Empty(dto.StrongTopics);
        Assert.Empty(dto.WeakTopics);
        Assert.Equal(0, dto.SampleSize.AssessedTopics);
    }

    // --- helpers ---

    // Topics are seeded straight via the domain factory, which only needs a non-empty trackId Guid —
    // no real Track row required (and no admin auth needed, unlike the CreateTrackAsync API helper).
    private static Task<Guid> SeedTrackAsync() => Task.FromResult(Guid.NewGuid());

    private Task<Guid> SeedTopicAsync(Guid trackId, string slug, string title) =>
        ExecuteInDbAsync(async db =>
        {
            Topic topic = Topic.Create(trackId, slug, title, "Runtime", null, null, "n").Value;
            await db.Topics.AddAsync(topic);
            await db.SaveChangesAsync();
            return topic.Id;
        });

    private Task SeedMasteryAsync(Guid userId, Guid topicId, int masteryPercent, int answersCount) =>
        ExecuteInDbAsync(async db =>
        {
            // Derived mastery row (#691) — strengths reads the stored value; seed it directly.
            TopicMastery mastery = TopicMastery.Create(userId, topicId);
            mastery.SetDerived(masteryPercent, answersCount, DateTime.UtcNow);

            await db.TopicMasteries.AddAsync(mastery);
            await db.SaveChangesAsync();
            return mastery.Id;
        });

    /// <summary>
    ///     Seeds one COMPLETED single-choice answer at <paramref name="difficulty"/> for (user, topic) —
    ///     used to satisfy the #691 strong-gate (a topic needs a MIDDLE/SENIOR answered question to be
    ///     crowned strong). Score/verdict don't matter for the gate, only that the answer exists.
    /// </summary>
    private Task SeedAnsweredQuestionAsync(Guid userId, Guid topicId, string difficulty) =>
        ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;
            session.AddItem(Guid.NewGuid(), topicId, "SINGLE_CHOICE", "Q", "[]", null, difficulty, 0, null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            session.RecordAnswer(session.Items[0].Id, "x", 100, AnswerVerdict.CORRECT, null);
            session.Complete(100);
            return await db.SaveChangesAsync();
        });

    private Task<Guid> SeedCompletedMockAsync(Guid userId, Guid topicId, string[]? weakTopics, string[]? strengths) =>
        SeedCompletedSessionAsync(userId, topicId, TrainingMode.MOCK, weakTopics, strengths);

    private Task<Guid> SeedCompletedDrillAsync(Guid userId, Guid topicId) =>
        SeedCompletedSessionAsync(userId, topicId, TrainingMode.DRILL, null, null);

    private async Task<Guid> SeedCompletedSessionAsync(
        Guid userId,
        Guid topicId,
        TrainingMode mode,
        string[]? weakTopics,
        string[]? strengths)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(userId, mode, trackId: null, [topicId]).Value;
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
            await db.SaveChangesAsync();

            session.RecordAnswer(session.Items[0].Id, "x", 100, AnswerVerdict.CORRECT, null);
            session.Complete(100);

            if (weakTopics is not null || strengths is not null)
                session.RecordAiOverall(
                    "overall",
                    weakTopics is null ? null : JsonSerializer.Serialize(weakTopics),
                    strengths is null ? null : JsonSerializer.Serialize(strengths));

            await db.SaveChangesAsync();
            return session.Id;
        });

        // Mocks need a completion date for GetCompletedMockSessionsAsync (CompletedAt != null).
        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_sessions SET completed_at = {DateTime.UtcNow} WHERE id = {sessionId}"));

        return sessionId;
    }
}
