using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordering;
using TrainerService.Contracts.Admin;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/question-quality?days=N (#681 T4): admin-only per-question quality
///     metrics over training_session_items joined to the trainer's own questions. Asserts %-correct,
///     skip-rate, attempts, OPEN_TEXT verdict split + score buckets, discrimination sign, time-per-question
///     approximation (with outlier/zero-diff guards), the window cutoff, days clamping, the empty case,
///     and that a non-admin gets 403.
/// </summary>
public sealed class QuestionQualityStatsTests(IntegrationTestsWebFactory factory)
    : TrainerServiceTestsBase(factory)
{
    private const string Url = "/trainer/admin/stats/question-quality";

    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync($"{Url}?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Empty_data_returns_no_questions()
    {
        AuthenticateAsAdmin();

        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        Assert.Equal(30, stats.Days);
        Assert.Equal(600, stats.OutlierCapSeconds);
        Assert.Empty(stats.Questions);
    }

    [Fact]
    public async Task Correctness_skip_rate_and_attempts_per_question()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid q1 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE, QuestionDifficulty.JUNIOR, "memory");

        // 3 correct + 1 incorrect (→ 75% correct), all in completed sessions. One more completed session
        // leaves q1 unanswered (skipped) → 1 / 5 completed items skipped.
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.INCORRECT, 0));
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, null, null)); // skipped

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto item = Assert.Single(stats.Questions);
        Assert.Equal(q1, item.QuestionId);
        Assert.Equal("SINGLE_CHOICE", item.QuestionType);
        Assert.Equal("JUNIOR", item.Difficulty);
        Assert.Equal("memory", item.Section);
        Assert.Equal(topicId, item.TopicId);
        Assert.Equal(bankId, item.BankId);

        Assert.Equal(4, item.Attempts);          // 3 correct + 1 incorrect answered; skipped not counted
        Assert.Equal(3, item.CorrectCount);
        Assert.Equal(0.75, item.CorrectRate!.Value, 3);

        Assert.Equal(5, item.CompletedItems);     // all five sessions completed
        Assert.Equal(1, item.SkippedItems);       // the one unanswered item
        Assert.Equal(0.2, item.SkipRate!.Value, 3);

        Assert.Null(item.OpenText);               // not an OPEN_TEXT question
    }

    [Fact]
    public async Task Open_text_verdict_split_and_score_buckets()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid qOpen = await SeedQuestionAsync(bankId, TrainerQuestionType.OPEN_TEXT, QuestionDifficulty.SENIOR);

        // verdicts: 2x CORRECT, 1x PARTIAL, 1x INCORRECT; scores cover all three grader bands.
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (qOpen, AnswerVerdict.CORRECT, 90));   // high
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (qOpen, AnswerVerdict.CORRECT, 85));   // high
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (qOpen, AnswerVerdict.PARTIAL, 60));   // mid
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (qOpen, AnswerVerdict.INCORRECT, 20)); // low

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto item = Assert.Single(stats.Questions);
        Assert.Equal("OPEN_TEXT", item.QuestionType);
        Assert.Equal(4, item.Attempts);
        Assert.Equal(2, item.CorrectCount);
        Assert.Equal(0.5, item.CorrectRate!.Value, 3);

        Assert.NotNull(item.OpenText);
        Assert.Equal(2, item.OpenText!.Correct);
        Assert.Equal(1, item.OpenText.Partial);
        Assert.Equal(1, item.OpenText.Incorrect);
        Assert.Equal(2, item.OpenText.ScoreBucketHigh); // 90, 85
        Assert.Equal(1, item.OpenText.ScoreBucketMid);  // 60
        Assert.Equal(1, item.OpenText.ScoreBucketLow);  // 20
    }

    [Fact]
    public async Task Score_buckets_respect_grader_band_boundaries()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid qOpen = await SeedQuestionAsync(bankId, TrainerQuestionType.OPEN_TEXT, QuestionDifficulty.SENIOR);

        // Exact band edges: 0/39 → low, 40/79 → mid, 80/100 → high (BETWEEN is inclusive on both ends).
        foreach (int score in new[] { 0, 39, 40, 79, 80, 100 })
            await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (qOpen, AnswerVerdict.CORRECT, score));

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminOpenTextQualityDto open = Assert.Single(stats.Questions).OpenText!;
        Assert.Equal(2, open.ScoreBucketLow);  // 0, 39
        Assert.Equal(2, open.ScoreBucketMid);  // 40, 79
        Assert.Equal(2, open.ScoreBucketHigh); // 80, 100
    }

    [Fact]
    public async Task Discrimination_is_null_when_not_enough_distinguishable_users()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid q1 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);

        // A single answering user → NTILE(2) yields only the bottom tile → no strong group → null.
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto item = Assert.Single(stats.Questions);
        Assert.Null(item.Discrimination);
        Assert.Null(item.TopGroupCorrectRate);
        Assert.Equal(1.0, item.BottomGroupCorrectRate!.Value, 3);
    }

    [Fact]
    public async Task Discrimination_separates_strong_from_weak_users()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid filler = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);
        Guid target = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE); // discriminates well
        Guid bad = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);    // anti-discriminates

        // Two strong users: filler + target correct, bad wrong → overall accuracy 2/3.
        // Two weak users:   filler + target wrong,   bad correct → overall accuracy 1/3.
        // NTILE(2) over accuracy → strong = top tile, weak = bottom tile.
        foreach (Guid strong in new[] { Guid.NewGuid(), Guid.NewGuid() })
            await SeedSessionAsync(strong, topicId, complete: true,
                (filler, AnswerVerdict.CORRECT, 100),
                (target, AnswerVerdict.CORRECT, 100),
                (bad, AnswerVerdict.INCORRECT, 0));

        foreach (Guid weak in new[] { Guid.NewGuid(), Guid.NewGuid() })
            await SeedSessionAsync(weak, topicId, complete: true,
                (filler, AnswerVerdict.INCORRECT, 0),
                (target, AnswerVerdict.INCORRECT, 0),
                (bad, AnswerVerdict.CORRECT, 100));

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto targetItem = Assert.Single(stats.Questions, q => q.QuestionId == target);
        Assert.Equal(1.0, targetItem.TopGroupCorrectRate!.Value, 3);    // strong got it right
        Assert.Equal(0.0, targetItem.BottomGroupCorrectRate!.Value, 3); // weak got it wrong
        Assert.Equal(1.0, targetItem.Discrimination!.Value, 3);         // positive — good separator
        Assert.True(targetItem.Discrimination > 0);

        AdminQuestionQualityItemDto badItem = Assert.Single(stats.Questions, q => q.QuestionId == bad);
        Assert.Equal(0.0, badItem.TopGroupCorrectRate!.Value, 3);       // strong got it wrong
        Assert.Equal(1.0, badItem.BottomGroupCorrectRate!.Value, 3);    // weak got it right
        Assert.Equal(-1.0, badItem.Discrimination!.Value, 3);           // negative — rewrite candidate
        Assert.True(badItem.Discrimination < 0);
    }

    [Fact]
    public async Task Time_per_question_approximates_gaps_and_drops_outliers_and_zero_diffs()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid q1 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);
        Guid q2 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);
        Guid q3 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);

        DateTime baseA = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Session A: q1@base, q2@+10s, q3@+30s → q2 diff 10s, q3 diff 20s, q1 first (no sample).
        (Guid _, IReadOnlyList<Guid> aItems) = await SeedSessionAsync(
            Guid.NewGuid(), topicId, complete: true,
            (q1, AnswerVerdict.CORRECT, 100),
            (q2, AnswerVerdict.CORRECT, 100),
            (q3, AnswerVerdict.CORRECT, 100));
        await SetAnsweredAtAsync(aItems[0], baseA);
        await SetAnsweredAtAsync(aItems[1], baseA.AddSeconds(10));
        await SetAnsweredAtAsync(aItems[2], baseA.AddSeconds(30));

        // Session B: q1 then q2 with a 100000s gap → q2 diff is an OUTLIER (> 600s cap) → dropped.
        DateTime baseB = new(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);
        (Guid _, IReadOnlyList<Guid> bItems) = await SeedSessionAsync(
            Guid.NewGuid(), topicId, complete: true,
            (q1, AnswerVerdict.CORRECT, 100),
            (q2, AnswerVerdict.CORRECT, 100));
        await SetAnsweredAtAsync(bItems[0], baseB);
        await SetAnsweredAtAsync(bItems[1], baseB.AddSeconds(100_000));

        // Session C: q1 then q2 at the SAME instant → q2 diff 0s → dropped (secs > 0 guard).
        DateTime baseC = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        (Guid _, IReadOnlyList<Guid> cItems) = await SeedSessionAsync(
            Guid.NewGuid(), topicId, complete: true,
            (q1, AnswerVerdict.CORRECT, 100),
            (q2, AnswerVerdict.CORRECT, 100));
        await SetAnsweredAtAsync(cItems[0], baseC);
        await SetAnsweredAtAsync(cItems[1], baseC);

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto i1 = Assert.Single(stats.Questions, q => q.QuestionId == q1);
        Assert.Null(i1.AvgSecondsPerQuestion); // always the first answer in its session → no predecessor
        Assert.Equal(0, i1.TimeSampleCount);

        AdminQuestionQualityItemDto i2 = Assert.Single(stats.Questions, q => q.QuestionId == q2);
        Assert.Equal(1, i2.TimeSampleCount);                   // only session A's 10s survives the guards
        Assert.Equal(10.0, i2.AvgSecondsPerQuestion!.Value, 3);

        AdminQuestionQualityItemDto i3 = Assert.Single(stats.Questions, q => q.QuestionId == q3);
        Assert.Equal(1, i3.TimeSampleCount);
        Assert.Equal(20.0, i3.AvgSecondsPerQuestion!.Value, 3);
    }

    [Fact]
    public async Task Window_excludes_sessions_started_before_cutoff()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid q1 = await SeedQuestionAsync(bankId, TrainerQuestionType.SINGLE_CHOICE);

        // One recent session (in window) and one started 60 days ago (out of the 30-day window).
        await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));
        (Guid oldSession, _) =
            await SeedSessionAsync(Guid.NewGuid(), topicId, complete: true, (q1, AnswerVerdict.CORRECT, 100));
        await BackdateSessionStartAsync(oldSession, DateTimeOffset.UtcNow.AddDays(-60));

        AuthenticateAsAdmin();
        AdminQuestionQualityStatsDto stats =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminQuestionQualityItemDto item = Assert.Single(stats.Questions);
        Assert.Equal(1, item.Attempts);     // only the recent session
        Assert.Equal(1, item.CorrectCount);
        Assert.Equal(1, item.CompletedItems);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        AdminQuestionQualityStatsDto dflt =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync(Url));
        Assert.Equal(30, dflt.Days);

        AdminQuestionQualityStatsDto clamped =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=99999"));
        Assert.Equal(365, clamped.Days);

        AdminQuestionQualityStatsDto floored =
            await ReadResultAsync<AdminQuestionQualityStatsDto>(await Client.GetAsync($"{Url}?days=0"));
        Assert.Equal(1, floored.Days);
    }

    // --- helpers ---

    private async Task<(Guid TopicId, Guid BankId)> SeedTopicWithBankAsync(string topicSlug = "quality-topic")
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, topicSlug, "Тема качества", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;

        return (topicId, bankId);
    }

    private int _sortCounter;

    /// <summary>Inserts one question of the given type into the bank via a fresh DbContext (real id back).</summary>
    private async Task<Guid> SeedQuestionAsync(
        Guid bankId,
        TrainerQuestionType type,
        QuestionDifficulty? difficulty = QuestionDifficulty.MIDDLE,
        string? section = null)
    {
        string sortKey = KeyAt(_sortCounter++);

        List<(string Text, bool IsCorrect)> options = type switch
        {
            TrainerQuestionType.SINGLE_CHOICE => [("Верно", true), ("Неверно", false)],
            TrainerQuestionType.MULTI_CHOICE => [("A", true), ("B", true), ("C", false)],
            _ => [],
        };
        string? reference =
            type is TrainerQuestionType.EXACT_TEXT or TrainerQuestionType.OPEN_TEXT ? "эталон" : null;

        return await ExecuteInDbAsync(async db =>
        {
            TrainerQuestion question = TrainerQuestion.Create(
                bankId, $"Вопрос {sortKey}", type, reference, "разбор", difficulty, section, sortKey, options).Value;
            db.TrainerQuestions.Add(question);
            await db.SaveChangesAsync();
            return question.Id;
        });
    }

    /// <summary>
    ///     Builds a session for <paramref name="userId"/> with one item per answer tuple. A null verdict
    ///     leaves the item unanswered (skipped). Returns the session id + ordered item ids (for time patching).
    /// </summary>
    private async Task<(Guid SessionId, IReadOnlyList<Guid> ItemIds)> SeedSessionAsync(
        Guid userId,
        Guid topicId,
        bool complete,
        params (Guid QuestionId, AnswerVerdict? Verdict, int? Score)[] answers) =>
        await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;

            int sort = 0;
            foreach ((Guid QuestionId, AnswerVerdict? Verdict, int? Score) a in answers)
            {
                // The query reads type/difficulty from trainer_questions, not the item snapshot —
                // so these snapshot fields are placeholders.
                session.AddItem(a.QuestionId, topicId, "SNAPSHOT", "snap", "[]", null, null, sort++, null);
            }

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            List<TrainingSessionItem> items = session.Items.OrderBy(i => i.SortIndex).ToList();
            for (int idx = 0; idx < answers.Length; idx++)
            {
                if (answers[idx].Verdict is { } verdict)
                    session.RecordAnswer(items[idx].Id, "ans", answers[idx].Score, verdict, null);
            }

            await db.SaveChangesAsync();

            if (complete)
            {
                session.Complete(100);
                await db.SaveChangesAsync();
            }

            return (session.Id, (IReadOnlyList<Guid>)items.Select(i => i.Id).ToList());
        });

    private Task SetAnsweredAtAsync(Guid itemId, DateTime answeredAtUtc) =>
        ExecuteInDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE trainer.training_session_items SET answered_at = {answeredAtUtc} WHERE id = {itemId}"));

    private Task BackdateSessionStartAsync(Guid sessionId, DateTimeOffset startedAt) =>
        ExecuteInDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE trainer.training_sessions SET started_at = {startedAt} WHERE id = {sessionId}"));

    private static string KeyAt(int index)
    {
        SortKey key = SortKey.Initial();
        for (int i = 0; i < index; i++)
            key = SortKey.After(key);

        return key.Value;
    }
}
