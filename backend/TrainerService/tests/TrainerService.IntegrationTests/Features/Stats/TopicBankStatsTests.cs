using System.Net;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Admin;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.Topics;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/topics?days=N (#681 T5): admin-only content analytics — per-topic mastery
///     snapshot + windowed %-correct/volume, per-bank coverage + type/difficulty composition, and difficulty
///     calibration (declared level vs actual %-correct + miscalibrated-question outliers). All data is seeded
///     directly through the domain. Covers: 403 for non-admin, per-topic happy path + hardest-first ordering +
///     zero-activity topic, per-bank coverage + empty bank, calibration levels (+UNSPECIFIED), miscalibration
///     outliers + min-answers gate, window exclusion, days clamp/default, and the empty-DB shape.
/// </summary>
public sealed class TopicBankStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private const string Url = "/trainer/admin/stats/topics";

    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync($"{Url}?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Empty_database_returns_empty_topics_banks_and_zero_filled_levels()
    {
        AuthenticateAsAdmin();

        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        Assert.Equal(30, stats.Days);
        Assert.Empty(stats.Topics);
        Assert.Empty(stats.Banks);
        Assert.Empty(stats.Calibration.Miscalibrated);

        // JUNIOR/MIDDLE/SENIOR always present, zero-filled; no UNSPECIFIED without null-difficulty answers.
        Assert.Equal(3, stats.Calibration.Levels.Count);
        Assert.Equal(["JUNIOR", "MIDDLE", "SENIOR"], stats.Calibration.Levels.Select(l => l.Difficulty));
        Assert.All(stats.Calibration.Levels, l =>
        {
            Assert.Equal(0d, l.ActualCorrectPercent);
            Assert.Equal(0, l.AnswersCount);
            Assert.Equal(0, l.QuestionsAnswered);
        });
    }

    [Fact]
    public async Task Per_topic_aggregates_mastery_correctness_and_volume()
    {
        Guid trackId = Guid.NewGuid();
        Guid topic = await SeedTopicAsync(trackId, "topic-a", "Тема A");

        // Mastery snapshot: two users at 80 and 40 → avg 60, two users.
        await SeedMasteryAsync(Guid.NewGuid(), topic, masteryPercent: 80, answersCount: 3);
        await SeedMasteryAsync(Guid.NewGuid(), topic, masteryPercent: 40, answersCount: 3);

        // Activity: session 1 = [100, 0], session 2 = [50] → 3 answers, avg 50, 2 distinct sessions.
        Guid q = Guid.NewGuid();
        await SeedSessionAsync(Guid.NewGuid(), (q, topic, "JUNIOR", 100), (q, topic, "JUNIOR", 0));
        await SeedSessionAsync(Guid.NewGuid(), (q, topic, "JUNIOR", 50));

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        AdminTopicStatDto t = Assert.Single(stats.Topics);
        Assert.Equal(topic, t.TopicId);
        Assert.Equal("Тема A", t.TopicTitle);
        Assert.Equal(60, t.AvgMasteryPercent);
        Assert.Equal(2, t.MasteryUsers);
        Assert.Equal(50d, t.AvgCorrectPercent, 2);
        Assert.Equal(3, t.AnswersCount);
        Assert.Equal(2, t.SessionsCount);
    }

    [Fact]
    public async Task Topics_are_ordered_hardest_first_with_mastery_only_topics_last()
    {
        Guid trackId = Guid.NewGuid();
        Guid hard = await SeedTopicAsync(trackId, "hard", "Трудная");
        Guid easy = await SeedTopicAsync(trackId, "easy", "Лёгкая");
        Guid masteryOnly = await SeedTopicAsync(trackId, "snapshot", "Только mastery");

        await SeedSessionAsync(Guid.NewGuid(), (Guid.NewGuid(), hard, "MIDDLE", 20));
        await SeedSessionAsync(Guid.NewGuid(), (Guid.NewGuid(), easy, "MIDDLE", 90));
        // Topic with a mastery snapshot but no answers in the window → zero activity, sorted last.
        await SeedMasteryAsync(Guid.NewGuid(), masteryOnly, masteryPercent: 70, answersCount: 4);

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        Assert.Equal(3, stats.Topics.Count);
        // Active topics first (hardest = lowest %-correct on top), then the mastery-only topic.
        Assert.Equal(hard, stats.Topics[0].TopicId);
        Assert.Equal(easy, stats.Topics[1].TopicId);
        Assert.Equal(masteryOnly, stats.Topics[2].TopicId);

        AdminTopicStatDto snapshot = stats.Topics[2];
        Assert.Equal(0, snapshot.AnswersCount);
        Assert.Equal(0, snapshot.SessionsCount);
        Assert.Equal(70, snapshot.AvgMasteryPercent); // mastery is an all-time snapshot, not windowed
    }

    [Fact]
    public async Task Per_bank_reports_coverage_and_type_difficulty_breakdown()
    {
        Guid trackId = Guid.NewGuid();
        Guid topic = await SeedTopicAsync(trackId, "topic-b", "Тема B");
        Guid bankFull = await SeedBankAsync(topic, BankTier.FREE, QuestionDifficulty.MIDDLE, BankPurpose.STUDY);
        Guid bankEmpty = await SeedBankAsync(topic);

        Guid q1 = await SeedQuestionAsync(bankFull, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "q1", "a");
        Guid q2 = await SeedQuestionAsync(bankFull, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "q2", "b");
        Guid q3 = await SeedQuestionAsync(bankFull, TrainerQuestionType.MULTI_CHOICE, QuestionDifficulty.MIDDLE, "q3", "c");
        _ = await SeedQuestionAsync(bankFull, TrainerQuestionType.OPEN_TEXT, QuestionDifficulty.SENIOR, "q4", "d");

        // Answer 2 of the 4 questions → coverage 50%. q4 (OPEN, SENIOR) stays unanswered.
        await SeedSessionAsync(Guid.NewGuid(), (q1, topic, "JUNIOR", 100), (q3, topic, "MIDDLE", 0));
        _ = q2;

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        Assert.Equal(2, stats.Banks.Count);

        AdminBankStatDto full = Assert.Single(stats.Banks, b => b.BankId == bankFull);
        Assert.Equal(topic, full.TopicId);
        Assert.Equal("Тема B", full.TopicTitle);
        Assert.Equal("FREE", full.Tier);
        Assert.Equal("MIDDLE", full.Difficulty);
        Assert.Equal("STUDY", full.Purpose);
        Assert.Equal(4, full.TotalQuestions);
        Assert.Equal(2, full.AnsweredQuestions);
        Assert.Equal(50d, full.CoveragePercent, 2);
        Assert.Equal(2, full.AnswersCount);

        Assert.Equal(2, full.ByType.Single(x => x.Type == "SINGLE_CHOICE").Count);
        Assert.Equal(1, full.ByType.Single(x => x.Type == "MULTI_CHOICE").Count);
        Assert.Equal(1, full.ByType.Single(x => x.Type == "OPEN_TEXT").Count);

        Assert.Equal(2, full.ByDifficulty.Single(x => x.Difficulty == "JUNIOR").Count);
        Assert.Equal(1, full.ByDifficulty.Single(x => x.Difficulty == "MIDDLE").Count);
        Assert.Equal(1, full.ByDifficulty.Single(x => x.Difficulty == "SENIOR").Count);

        // Empty bank: zero coverage, empty breakdowns — surfaces «банк простаивает».
        AdminBankStatDto empty = Assert.Single(stats.Banks, b => b.BankId == bankEmpty);
        Assert.Equal(0, empty.TotalQuestions);
        Assert.Equal(0, empty.AnsweredQuestions);
        Assert.Equal(0d, empty.CoveragePercent);
        Assert.Empty(empty.ByType);
        Assert.Empty(empty.ByDifficulty);
    }

    [Fact]
    public async Task Calibration_reports_actual_correctness_per_declared_level()
    {
        Guid trackId = Guid.NewGuid();
        Guid topic = await SeedTopicAsync(trackId, "topic-c", "Тема C");
        Guid bank = await SeedBankAsync(topic);

        Guid junior = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "j", "a");
        Guid middle = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.MIDDLE, "m", "b");
        Guid senior = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.SENIOR, "s", "c");
        Guid unspec = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, difficulty: null, "u", "d");

        await SeedSessionAsync(Guid.NewGuid(),
            (junior, topic, "JUNIOR", 100), (junior, topic, "JUNIOR", 80),   // JUNIOR avg 90
            (middle, topic, "MIDDLE", 70), (middle, topic, "MIDDLE", 50),    // MIDDLE avg 60
            (senior, topic, "SENIOR", 40), (senior, topic, "SENIOR", 20),    // SENIOR avg 30
            (unspec, topic, null, 50), (unspec, topic, null, 50));           // UNSPECIFIED avg 50

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        IReadOnlyList<AdminCalibrationLevelDto> levels = stats.Calibration.Levels;
        Assert.Equal(["JUNIOR", "MIDDLE", "SENIOR", "UNSPECIFIED"], levels.Select(l => l.Difficulty));

        AdminCalibrationLevelDto j = levels.Single(l => l.Difficulty == "JUNIOR");
        Assert.Equal(90d, j.ActualCorrectPercent, 2);
        Assert.Equal(2, j.AnswersCount);
        Assert.Equal(1, j.QuestionsAnswered);

        Assert.Equal(60d, levels.Single(l => l.Difficulty == "MIDDLE").ActualCorrectPercent, 2);
        Assert.Equal(30d, levels.Single(l => l.Difficulty == "SENIOR").ActualCorrectPercent, 2);
        Assert.Equal(50d, levels.Single(l => l.Difficulty == "UNSPECIFIED").ActualCorrectPercent, 2);
    }

    [Fact]
    public async Task Calibration_surfaces_miscalibrated_question_outliers()
    {
        Guid trackId = Guid.NewGuid();
        Guid topic = await SeedTopicAsync(trackId, "topic-d", "Тема D");
        Guid bank = await SeedBankAsync(topic);

        // Two JUNIOR questions: one always wrong (hard for its label), one always right (easy for its label).
        // Cohort JUNIOR avg = (0+0+100+100)/4 = 50 → deltas -50 and +50.
        Guid hardJunior = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "hard-junior", "a");
        Guid easyJunior = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "easy-junior", "b");
        // A SENIOR question answered only ONCE → below MIN_ANSWERS_FOR_CALIBRATION (2) → excluded from outliers.
        Guid loneSenior = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.SENIOR, "lone-senior", "c");

        await SeedSessionAsync(Guid.NewGuid(),
            (hardJunior, topic, "JUNIOR", 0), (hardJunior, topic, "JUNIOR", 0),
            (easyJunior, topic, "JUNIOR", 100), (easyJunior, topic, "JUNIOR", 100),
            (loneSenior, topic, "SENIOR", 0));

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        IReadOnlyList<AdminMiscalibratedQuestionDto> outliers = stats.Calibration.Miscalibrated;
        Assert.Equal(2, outliers.Count);
        Assert.DoesNotContain(outliers, q => q.QuestionId == loneSenior);

        AdminMiscalibratedQuestionDto hard = Assert.Single(outliers, q => q.QuestionId == hardJunior);
        Assert.Equal("JUNIOR", hard.Difficulty);
        Assert.Equal("Тема D", hard.TopicTitle);
        Assert.Equal(bank, hard.BankId);
        Assert.Equal(0d, hard.ActualCorrectPercent, 2);
        Assert.Equal(-50d, hard.DeltaVsLevel, 2); // far below its JUNIOR cohort → really harder than its label
        Assert.Equal(2, hard.AnswersCount);
        Assert.Equal("hard-junior", hard.Stem);

        AdminMiscalibratedQuestionDto easy = Assert.Single(outliers, q => q.QuestionId == easyJunior);
        Assert.Equal(100d, easy.ActualCorrectPercent, 2);
        Assert.Equal(50d, easy.DeltaVsLevel, 2); // far above its JUNIOR cohort → really easier than its label
    }

    [Fact]
    public async Task Window_excludes_answers_older_than_days()
    {
        Guid trackId = Guid.NewGuid();
        Guid topic = await SeedTopicAsync(trackId, "topic-e", "Тема E");
        Guid bank = await SeedBankAsync(topic);
        Guid q = await SeedQuestionAsync(bank, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "q", "a");

        // Recent answer (in window) scores 100; old answer (60 days back) scores 0 — must be excluded.
        await SeedSessionAsync(Guid.NewGuid(), (q, topic, "JUNIOR", 100));
        Guid oldSession = await SeedSessionAsync(Guid.NewGuid(), (q, topic, "JUNIOR", 0));
        await BackdateAnswersAsync(oldSession, DateTime.UtcNow.AddDays(-60));

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        // Topic activity: only the recent 100 counts.
        AdminTopicStatDto t = Assert.Single(stats.Topics);
        Assert.Equal(100d, t.AvgCorrectPercent, 2);
        Assert.Equal(1, t.AnswersCount);
        Assert.Equal(1, t.SessionsCount);

        // Bank coverage: only the recent answer.
        AdminBankStatDto b = Assert.Single(stats.Banks);
        Assert.Equal(1, b.AnsweredQuestions);
        Assert.Equal(1, b.AnswersCount);

        // Calibration JUNIOR: only the recent 100.
        Assert.Equal(100d, stats.Calibration.Levels.Single(l => l.Difficulty == "JUNIOR").ActualCorrectPercent, 2);
        Assert.Equal(1, stats.Calibration.Levels.Single(l => l.Difficulty == "JUNIOR").AnswersCount);
    }

    [Fact]
    public async Task Topic_without_a_topics_row_falls_back_to_default_title()
    {
        // Orphan: a mastery row whose topic row no longer exists (topic deleted while it had no banks).
        // The title resolver must fall back to «Тема» instead of dropping the row.
        Guid orphanTopic = Guid.NewGuid();
        await SeedMasteryAsync(Guid.NewGuid(), orphanTopic, masteryPercent: 50, answersCount: 3);

        AuthenticateAsAdmin();
        AdminTopicBankStatsDto stats = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));

        AdminTopicStatDto t = Assert.Single(stats.Topics);
        Assert.Equal(orphanTopic, t.TopicId);
        Assert.Equal("Тема", t.TopicTitle);
        Assert.Equal(50, t.AvgMasteryPercent);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        AdminTopicBankStatsDto dflt = await ReadResultAsync<AdminTopicBankStatsDto>(await Client.GetAsync(Url));
        Assert.Equal(30, dflt.Days);

        AdminTopicBankStatsDto clamped = await ReadResultAsync<AdminTopicBankStatsDto>(
            await Client.GetAsync($"{Url}?days=99999"));
        Assert.Equal(365, clamped.Days);

        AdminTopicBankStatsDto floored = await ReadResultAsync<AdminTopicBankStatsDto>(
            await Client.GetAsync($"{Url}?days=0"));
        Assert.Equal(1, floored.Days);
    }

    // --- helpers ---

    private Task<Guid> SeedTopicAsync(Guid trackId, string slug, string title) =>
        ExecuteInDbAsync(async db =>
        {
            Topic topic = Topic.Create(trackId, slug, title, "Runtime", null, null, "n").Value;
            await db.Topics.AddAsync(topic);
            await db.SaveChangesAsync();
            return topic.Id;
        });

    private Task<Guid> SeedBankAsync(
        Guid topicId,
        BankTier tier = BankTier.FREE,
        QuestionDifficulty? difficulty = null,
        BankPurpose purpose = BankPurpose.STUDY) =>
        ExecuteInDbAsync(async db =>
        {
            TopicBank bank = TopicBank.Create(topicId, tier, difficulty, "n", purpose).Value;
            db.TopicBanks.Add(bank);
            await db.SaveChangesAsync();
            return bank.Id;
        });

    private Task<Guid> SeedQuestionAsync(
        Guid bankId,
        TrainerQuestionType type,
        QuestionDifficulty? difficulty,
        string stem,
        string sortKey) =>
        ExecuteInDbAsync(async db =>
        {
            IReadOnlyList<(string, bool)> options =
                type is TrainerQuestionType.SINGLE_CHOICE or TrainerQuestionType.MULTI_CHOICE
                    ? [("A", true), ("B", false)]
                    : [];
            string? reference = type is TrainerQuestionType.EXACT_TEXT or TrainerQuestionType.OPEN_TEXT ? "ref" : null;

            TrainerQuestion question = TrainerQuestion.Create(
                bankId, stem, type, reference, null, difficulty, null, sortKey, options).Value;
            db.TrainerQuestions.Add(question);
            await db.SaveChangesAsync();
            return question.Id;
        });

    private Task SeedMasteryAsync(Guid userId, Guid topicId, int masteryPercent, int answersCount) =>
        ExecuteInDbAsync(async db =>
        {
            // Derived mastery row (#691) — stats reads the stored snapshot value; seed it directly.
            TopicMastery mastery = TopicMastery.Create(userId, topicId);
            mastery.SetDerived(masteryPercent, answersCount, DateTime.UtcNow);

            await db.TopicMasteries.AddAsync(mastery);
            await db.SaveChangesAsync();
            return mastery.Id;
        });

    /// <summary>
    ///     Seeds one COMPLETED session with the given answered items (each: questionId, topicId, item difficulty,
    ///     score). Item ids are EF-generated, so answers are recorded after the first save (same instance keeps
    ///     insertion order). Verdict follows the score (>=50 → CORRECT) — only the score drives the stats.
    /// </summary>
    private Task<Guid> SeedSessionAsync(
        Guid userId,
        params (Guid questionId, Guid topicId, string? difficulty, int score)[] answers) =>
        ExecuteInDbAsync(async db =>
        {
            Guid[] topicIds = answers.Select(a => a.topicId).Distinct().ToArray();
            TrainingSession session = TrainingSession.Create(userId, TrainingMode.DRILL, trackId: null, topicIds).Value;

            int sortIndex = 0;
            foreach ((Guid questionId, Guid topicId, string? difficulty, int _) in answers)
                session.AddItem(questionId, topicId, "SINGLE_CHOICE", "Q", "[]", null, difficulty, sortIndex++, null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            for (int i = 0; i < answers.Length; i++)
            {
                int score = answers[i].score;
                AnswerVerdict verdict = score >= 50 ? AnswerVerdict.CORRECT : AnswerVerdict.INCORRECT;
                session.RecordAnswer(session.Items[i].Id, "x", score, verdict, null);
            }

            session.Complete(100);
            await db.SaveChangesAsync();
            return session.Id;
        });

    private Task BackdateAnswersAsync(Guid sessionId, DateTime answeredAt) =>
        ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_session_items SET answered_at = {answeredAt} WHERE session_id = {sessionId}"));
}
