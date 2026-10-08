using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Cross-topic SRS due-queue + «Мои ошибки» (#568 Ф2): own-data scoping, stem-enrichment from
///     the local question bank, due-only filter (next_due_at &lt;= now), WRONG/REVIEW filter,
///     difficulty filter.
/// </summary>
public sealed class SrsAndMistakesTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // --- SRS due-queue ---

    [Fact]
    public async Task DueQueue_returns_only_questions_due_now_with_stem()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        // Grade two questions → both scheduled in the FUTURE (SM-2: due tomorrow). Neither is due now.
        await SubmitGradeAsync(q.SingleQuestionId, topicId, knew: true);
        await SubmitGradeAsync(q.MultiQuestionId, topicId, knew: false);

        IReadOnlyList<SrsDueItemDto> empty = await GetDueAsync();
        Assert.Empty(empty); // nothing due yet — both scheduled for tomorrow

        // Force the SINGLE question's next_due_at into the past → it becomes due.
        await SetNextDueInPastAsync(CurrentUserId, q.SingleQuestionId);

        IReadOnlyList<SrsDueItemDto> due = await GetDueAsync();
        SrsDueItemDto item = Assert.Single(due);
        Assert.Equal(q.SingleQuestionId, item.QuestionId);
        Assert.Equal(topicId, item.TopicId);
        Assert.NotNull(item.Stem); // enriched from the local question bank (PRO → not redacted)
    }

    [Fact]
    public async Task DueQueue_is_scoped_to_caller()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");
        await SubmitGradeAsync(q.SingleQuestionId, topicId, knew: true);
        await SetNextDueInPastAsync(CurrentUserId, q.SingleQuestionId);

        // Another user has no study-state → empty due-queue.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        IReadOnlyList<SrsDueItemDto> due = await GetDueAsync();
        Assert.Empty(due);
    }

    // --- mistakes ---

    [Fact]
    public async Task Mistakes_returns_wrong_questions_with_stem_and_counters()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        await SubmitGradeAsync(q.SingleQuestionId, topicId, knew: false); // WRONG
        await SubmitGradeAsync(q.MultiQuestionId, topicId, knew: true);   // KNOWN — not a mistake

        IReadOnlyList<MistakeItemDto> mistakes = await GetMistakesAsync();
        MistakeItemDto item = Assert.Single(mistakes);
        Assert.Equal(q.SingleQuestionId, item.QuestionId);
        Assert.Equal("WRONG", item.Status);
        Assert.Equal(1, item.TimesWrong);
        Assert.NotNull(item.Stem);
    }

    [Fact]
    public async Task Mistakes_filters_by_difficulty()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        // SINGLE is JUNIOR, MULTI is MIDDLE — both wrong.
        await SubmitGradeAsync(q.SingleQuestionId, topicId, knew: false);
        await SubmitGradeAsync(q.MultiQuestionId, topicId, knew: false);

        HttpResponseMessage response = await Client.GetAsync("/trainer/mistakes?difficulty=MIDDLE");
        IReadOnlyList<MistakeItemDto> middle = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(response);
        MistakeItemDto only = Assert.Single(middle);
        Assert.Equal(q.MultiQuestionId, only.QuestionId);
    }

    [Fact]
    public async Task Mistakes_is_scoped_to_caller()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");
        await SubmitGradeAsync(q.SingleQuestionId, topicId, knew: false);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        IReadOnlyList<MistakeItemDto> mistakes = await GetMistakesAsync();
        Assert.Empty(mistakes);
    }

    // --- helpers ---

    private async Task<IReadOnlyList<SrsDueItemDto>> GetDueAsync()
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/srs/due");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<IReadOnlyList<SrsDueItemDto>>(response);
    }

    private async Task<IReadOnlyList<MistakeItemDto>> GetMistakesAsync()
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/mistakes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(response);
    }

    /// <summary>
    ///     Seeds a <see cref="QuestionStudyState"/> for the current caller directly via the domain
    ///     factory + the surviving <see cref="QuestionStudyState.RecordTestResult"/> path (KNOWN on
    ///     correct, WRONG otherwise — drives the same SM-2 scheduler the SRS/mistakes reads use).
    /// </summary>
    private Task SubmitGradeAsync(Guid questionId, Guid topicId, bool knew) =>
        ExecuteInDbAsync(async db =>
        {
            QuestionStudyState state = QuestionStudyState.Create(CurrentUserId, questionId, topicId);
            state.RecordTestResult(knew, DateTimeOffset.UtcNow);
            await db.QuestionStudyStates.AddAsync(state);
            await db.SaveChangesAsync();
            return state.Id;
        });

    /// <summary>
    ///     Pushes a study-state's <c>next_due_at</c> into the past so the SRS due-queue surfaces it.
    ///     SM-2 always schedules at least a day out, so we can't trip the due-filter through the API.
    /// </summary>
    private Task SetNextDueInPastAsync(Guid userId, Guid questionId) =>
        ExecuteInDbAsync(async db =>
            await db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.question_study_states SET next_due_at = {DateTimeOffset.UtcNow.AddDays(-1)} WHERE user_id = {userId} AND question_id = {questionId}"));

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "srs-topic", "Тема SRS", "Runtime", null, null, null, null));
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
