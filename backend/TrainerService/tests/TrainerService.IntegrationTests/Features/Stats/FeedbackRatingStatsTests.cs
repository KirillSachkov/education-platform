using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordering;
using TrainerService.Contracts.Admin;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.FeedbackRatings;
using TrainerService.Domain.Questions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/feedback-ratings?days=N (#691 t7): admin-only per-question 👍/👎 of the
///     AI разбор over <c>ai_feedback_ratings</c> joined to the trainer's own questions. Asserts the
///     up/down counts (two users rating the same question), down-rate, worst-first ordering, the window
///     cutoff, days clamping, the empty case, and that a non-admin gets 403.
/// </summary>
public sealed class FeedbackRatingStatsTests(IntegrationTestsWebFactory factory)
    : TrainerServiceTestsBase(factory)
{
    private const string Url = "/trainer/admin/stats/feedback-ratings";

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

        AdminFeedbackRatingStatsDto stats =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        Assert.Equal(30, stats.Days);
        Assert.Empty(stats.Questions);
    }

    [Fact]
    public async Task Per_question_up_down_counts_with_two_users_and_worst_first_ordering()
    {
        (Guid topicId, Guid bankId) = await SeedTopicWithBankAsync();
        Guid qMixed = await SeedQuestionAsync(bankId, "Опишите GC.", QuestionDifficulty.SENIOR);
        Guid qBad = await SeedQuestionAsync(bankId, "Опишите async.", QuestionDifficulty.MIDDLE);

        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();

        // qMixed: 1 UP + 1 DOWN → downRate 0.5. qBad: 2 DOWN → downRate 1.0 (worst → first).
        await SeedRatingAsync(user1, qMixed, FeedbackRating.UP);
        await SeedRatingAsync(user2, qMixed, FeedbackRating.DOWN);
        await SeedRatingAsync(user1, qBad, FeedbackRating.DOWN);
        await SeedRatingAsync(user2, qBad, FeedbackRating.DOWN);

        AuthenticateAsAdmin();
        AdminFeedbackRatingStatsDto stats =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        Assert.Equal(2, stats.Questions.Count);

        // Worst-first: the all-DOWN question leads.
        AdminFeedbackRatingItemDto first = stats.Questions[0];
        Assert.Equal(qBad, first.QuestionId);
        Assert.Equal(0, first.Up);
        Assert.Equal(2, first.Down);
        Assert.Equal(2, first.Total);
        Assert.Equal(1.0, first.DownRate, 3);
        Assert.Equal(topicId, first.TopicId);
        Assert.Equal(bankId, first.BankId);
        Assert.Equal("MIDDLE", first.Difficulty);

        AdminFeedbackRatingItemDto second = stats.Questions[1];
        Assert.Equal(qMixed, second.QuestionId);
        Assert.Equal(1, second.Up);
        Assert.Equal(1, second.Down);
        Assert.Equal(2, second.Total);
        Assert.Equal(0.5, second.DownRate, 3);
    }

    [Fact]
    public async Task Window_excludes_ratings_created_before_cutoff()
    {
        (Guid _, Guid bankId) = await SeedTopicWithBankAsync();
        Guid q1 = await SeedQuestionAsync(bankId, "Вопрос окна", QuestionDifficulty.JUNIOR);

        await SeedRatingAsync(Guid.NewGuid(), q1, FeedbackRating.DOWN);                 // in window
        Guid oldRatingUser = Guid.NewGuid();
        await SeedRatingAsync(oldRatingUser, q1, FeedbackRating.UP);                    // will be backdated out
        await BackdateRatingAsync(oldRatingUser, q1, DateTimeOffset.UtcNow.AddDays(-60));

        AuthenticateAsAdmin();
        AdminFeedbackRatingStatsDto stats =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync($"{Url}?days=30"));

        AdminFeedbackRatingItemDto item = Assert.Single(stats.Questions);
        Assert.Equal(0, item.Up);          // the old UP is outside the 30-day window
        Assert.Equal(1, item.Down);
        Assert.Equal(1, item.Total);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        AdminFeedbackRatingStatsDto dflt =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync(Url));
        Assert.Equal(30, dflt.Days);

        AdminFeedbackRatingStatsDto clamped =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync($"{Url}?days=99999"));
        Assert.Equal(365, clamped.Days);

        AdminFeedbackRatingStatsDto floored =
            await ReadResultAsync<AdminFeedbackRatingStatsDto>(await Client.GetAsync($"{Url}?days=0"));
        Assert.Equal(1, floored.Days);
    }

    // --- helpers ---

    private async Task<(Guid TopicId, Guid BankId)> SeedTopicWithBankAsync(string topicSlug = "rating-stats-topic")
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, topicSlug, "Тема оценок", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;

        return (topicId, bankId);
    }

    private int _sortCounter;

    private async Task<Guid> SeedQuestionAsync(Guid bankId, string stem, QuestionDifficulty difficulty)
    {
        string sortKey = KeyAt(_sortCounter++);
        return await ExecuteInDbAsync(async db =>
        {
            TrainerQuestion question = TrainerQuestion.Create(
                bankId, stem, TrainerQuestionType.OPEN_TEXT, "эталон", "разбор", difficulty, null, sortKey, [])
                .Value;
            db.TrainerQuestions.Add(question);
            await db.SaveChangesAsync();
            return question.Id;
        });
    }

    private Task SeedRatingAsync(Guid userId, Guid questionId, FeedbackRating rating) =>
        ExecuteInDbAsync(async db =>
        {
            // session/item ids are arbitrary (no FK) — the admin aggregate only reads question_id.
            AiFeedbackRating row = AiFeedbackRating.Create(
                userId, Guid.NewGuid(), Guid.NewGuid(), questionId, rating);
            db.AiFeedbackRatings.Add(row);
            return await db.SaveChangesAsync();
        });

    private Task BackdateRatingAsync(Guid userId, Guid questionId, DateTimeOffset createdAt) =>
        ExecuteInDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE trainer.ai_feedback_ratings SET created_at = {createdAt} WHERE user_id = {userId} AND question_id = {questionId}"));

    private static string KeyAt(int index)
    {
        SortKey key = SortKey.Initial();
        for (int i = 0; i < index; i++)
            key = SortKey.After(key);

        return key.Value;
    }
}
