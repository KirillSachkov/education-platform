using System.Net.Http.Json;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Progress;

/// <summary>
///     GET /trainer/progress per-topic study-analytics extension (#568 Ф2): studiedCount /
///     mistakesCount sourced from <c>QuestionStudyState</c> even when no <c>TopicMastery</c> row
///     exists (study-state doesn't touch mastery). Own-data only.
/// </summary>
public sealed class ProgressAnalyticsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Progress_reports_studied_and_mistakes_counts_from_study_states()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid userId = Guid.NewGuid();

        // Three study-states: 2 known, 1 wrong → studied=3, mistakes=1. No mastery row is created
        // (study-state feeds the analytics counters, not TopicMastery).
        await SeedStudyStateAsync(userId, q.SingleQuestionId, topicId, correct: true);
        await SeedStudyStateAsync(userId, q.MultiQuestionId, topicId, correct: true);
        await SeedStudyStateAsync(userId, q.ExactQuestionId, topicId, correct: false);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(response);

        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(3, topic.StudiedCount);
        Assert.Equal(1, topic.MistakesCount);
        Assert.Equal(0, topic.MasteryPercent); // mastery untouched by study-state
        Assert.Equal(0, topic.AnswersCount);
    }

    [Fact]
    public async Task Progress_is_scoped_to_caller()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        await SeedStudyStateAsync(Guid.NewGuid(), q.SingleQuestionId, topicId, correct: false);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(response);
        Assert.Empty(progress.Mastery);
    }

    // --- helpers ---

    /// <summary>
    ///     Seeds a <see cref="QuestionStudyState"/> directly via the domain factory + the surviving
    ///     <see cref="QuestionStudyState.RecordTestResult"/> path (KNOWN on correct, WRONG otherwise).
    /// </summary>
    private Task SeedStudyStateAsync(Guid userId, Guid questionId, Guid topicId, bool correct) =>
        ExecuteInDbAsync(async db =>
        {
            QuestionStudyState state = QuestionStudyState.Create(userId, questionId, topicId);
            state.RecordTestResult(correct, DateTimeOffset.UtcNow);
            await db.QuestionStudyStates.AddAsync(state);
            await db.SaveChangesAsync();
            return state.Id;
        });

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "progress-topic", "Тема прогресса", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, questions);
    }
}
