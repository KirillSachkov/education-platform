using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     «Изучение» (#568 Ф2): список вопросов охвата (no answer-key leak, статусы, фильтры,
///     анонимный доступ к free-теме, PRO-замок).
/// </summary>
public sealed class QuestionStudyTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // --- question list ---

    [Fact]
    public async Task GetQuestionList_returns_metadata_without_answers_and_marks_new_status()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);

        Assert.False(list.IsLocked);
        Assert.Equal(4, list.Items.Count);
        Assert.All(list.Items, i =>
        {
            Assert.Equal("NEW", i.Status); // no study-state yet
            Assert.False(i.IsBookmarked);
            Assert.NotNull(i.Stem);
        });

        // No-leak invariant: the question-list body must not carry the correct-answer fields or
        // the reference texts / explanations of any question.
        string raw = await ReadRawAsync(response);
        Assert.DoesNotContain("correctOptionIds", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceAnswer", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.SingleExplanation, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuestionList_is_anonymous_for_free_topic()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        RemoveAuthentication(); // SEO: anonymous list of a free topic.

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);

        Assert.False(list.IsLocked);
        Assert.Equal(4, list.Items.Count);
        Assert.All(list.Items, i => Assert.Equal("NEW", i.Status)); // anon has no personal state
    }

    [Fact]
    public async Task GetQuestionList_redacts_locked_items_for_free_participant_but_lists_metadata()
    {
        (Guid topicId, var q) = await SeedPublishedTopicAsync("PAID");
        AuthenticateAs("platform-participant"); // non-admin
        EntitlementChecker.DenyAll(); // free participant — no cap:TRAINER_PRO

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);

        // Per-question access (#674): bank-tier is dormant, so the topic is NOT fully locked — it has
        // free samples (single, multi). All 4 items are listed; locked ones have their stem redacted.
        Assert.False(list.IsLocked);
        Assert.Equal(4, list.Items.Count);

        QuestionListItemDto single = list.Items.Single(i => i.QuestionId == q.SingleQuestionId);
        Assert.False(single.IsLocked);
        Assert.NotNull(single.Stem); // free sample keeps its stem

        QuestionListItemDto open = list.Items.Single(i => i.QuestionId == q.OpenQuestionId);
        Assert.True(open.IsLocked); // OPEN_TEXT is never a free sample
        Assert.Equal("pro_required", open.LockReason);
        Assert.Null(open.Stem);          // server-side redaction
        Assert.Equal("OPEN_TEXT", open.Type); // safe metadata still present

        // No-leak: the body never carries the reference answers / explanations of locked questions.
        string raw = await ReadRawAsync(response);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuestionList_filters_by_difficulty_and_type()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage byDifficulty = await Client.GetAsync($"/trainer/topics/{topicId}/questions?difficulty=JUNIOR");
        QuestionListDto junior = await ReadResultAsync<QuestionListDto>(byDifficulty);
        Assert.Equal(2, junior.Items.Count); // SINGLE + EXACT are JUNIOR in the fixture
        Assert.All(junior.Items, i => Assert.Equal("JUNIOR", i.Difficulty));

        HttpResponseMessage byType = await Client.GetAsync($"/trainer/topics/{topicId}/questions?type=OPEN_TEXT");
        QuestionListDto open = await ReadResultAsync<QuestionListDto>(byType);
        QuestionListItemDto only = Assert.Single(open.Items);
        Assert.Equal("OPEN_TEXT", only.Type);
    }

    [Fact]
    public async Task GetQuestionList_tag_filter_matches_nothing_until_content_tags_exist()
    {
        // Tags are a follow-up (questions carry no tags yet); the param is plumbed but currently
        // filters everything out.
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions?tag=async");
        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task GetQuestionList_reflects_study_state_status()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid userId = Guid.NewGuid();
        await SeedStudyStateAsync(userId, q.SingleQuestionId, topicId, correct: true);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions?status=KNOWN");
        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);

        QuestionListItemDto known = Assert.Single(list.Items);
        Assert.Equal(q.SingleQuestionId, known.QuestionId);
        Assert.Equal("KNOWN", known.Status);
    }

    // --- helpers ---

    /// <summary>
    ///     Seeds a <see cref="QuestionStudyState"/> directly via the domain factory + the surviving
    ///     <see cref="QuestionStudyState.RecordTestResult"/> path (drives the same SM-2 scheduler that
    ///     the question-list / SRS / mistakes / progress projections read).
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

    /// <summary>Seeds a published topic with a FREE bank (canonical 4 local questions). Leaves client admin.</summary>
    private Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync() =>
        SeedPublishedTopicAsync("FREE");

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedTopicAsync(string tier)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "study-topic", "Тема изучения", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, questions);
    }
}
