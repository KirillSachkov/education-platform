using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.MockInterviews;

/// <summary>
///     Author-curated mock-interview constructor (#585): admin curates exact questions + a per-session
///     draw size; student sessions draw a random subset; the builder/picker resolve stems from the
///     local question bank; dangling refs (deleted question) are skipped, not fatal.
/// </summary>
public sealed class MockInterviewBuilderTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Student_session_draws_exactly_per_session_random_subset_from_curated_pool()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 2);

        // Pool = 4 curated questions, QuestionsPerSession = 2 → student gets exactly 2, all from the set.
        AuthenticateAs("platform-participant");
        SessionDto session = await StartInterviewSessionAsync(curated.MockInterviewId);

        Assert.Equal("MOCK", session.Mode);
        Assert.Equal("END_OF_SESSION", session.RevealPolicy);
        Assert.Equal(2, session.Items.Count);
        Assert.All(session.Items, i => Assert.Contains(i.QuestionId, curated.QuestionIds));

        // No-leak: the start response must not carry grading-key blobs nor secret reference texts.
        string raw = await ReadRawAsync(await StartRawAsync(curated.MockInterviewId));
        Assert.DoesNotContain("gradingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Student_session_draws_whole_curated_set_when_per_session_is_null()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: null);

        AuthenticateAs("platform-participant");
        SessionDto session = await StartInterviewSessionAsync(curated.MockInterviewId);

        // No draw cap → all 4 curated questions, each exactly once.
        Assert.Equal(4, session.Items.Count);
        Assert.Equal(
            curated.QuestionIds.OrderBy(g => g),
            session.Items.Select(i => i.QuestionId).OrderBy(g => g));
    }

    [Fact]
    public async Task Builder_returns_curated_questions_with_resolved_stems()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 3);

        HttpResponseMessage response = await Client.GetAsync($"/trainer/mock-interviews/{curated.MockInterviewId}/builder");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MockInterviewBuilderDto builder = await ReadResultAsync<MockInterviewBuilderDto>(response);

        Assert.Equal(3, builder.QuestionsPerSession);
        Assert.True(builder.IsPublished);
        Assert.Equal(4, builder.Questions.Count);

        // Stems are resolved from the local question bank and topic-attributed via the bank.
        MockInterviewBuilderQuestionDto single =
            Assert.Single(builder.Questions, q => q.QuestionId == curated.Questions.SingleQuestionId);
        Assert.Equal(TrainerQuestionFixtures.SingleStem, single.Text);
        Assert.Equal("SINGLE_CHOICE", single.Type);
        Assert.Equal(curated.TopicId, single.TopicId);
        Assert.Equal("Тема собеса", single.TopicTitle);

        // SortIndex order preserved (curated in fixture order).
        Assert.Equal(
            curated.QuestionIds,
            builder.Questions.Select(q => q.QuestionId).ToArray());
    }

    [Fact]
    public async Task QuestionBank_lists_available_questions_with_topic_and_track()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 2);

        HttpResponseMessage response = await Client.GetAsync("/trainer/question-bank");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuestionBankItemDto> items =
            await ReadResultAsync<IReadOnlyList<QuestionBankItemDto>>(response);

        // The seeded published topic's bank contributes its 4 questions.
        Assert.Equal(4, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(curated.BankId, i.BankId);
            Assert.Equal(curated.TopicId, i.TopicId);
            Assert.Equal("Тема собеса", i.TopicTitle);
            Assert.Equal(curated.TrackId, i.TrackId);
            Assert.Equal(".NET", i.TrackTitle);
        });
        Assert.Contains(items, i => i.QuestionId == curated.Questions.SingleQuestionId);
    }

    [Fact]
    public async Task QuestionBank_filters_by_topic()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 2);

        // Unknown topic id → empty list (filter excludes the seeded topic).
        HttpResponseMessage response = await Client.GetAsync($"/trainer/question-bank?topicId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadResultAsync<IReadOnlyList<QuestionBankItemDto>>(response));

        // The seeded topic id → its questions.
        HttpResponseMessage scoped = await Client.GetAsync($"/trainer/question-bank?topicId={curated.TopicId}");
        Assert.Equal(4, (await ReadResultAsync<IReadOnlyList<QuestionBankItemDto>>(scoped)).Count);
    }

    [Fact]
    public async Task Dangling_curated_ref_is_skipped_not_fatal()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: null);

        // Curate one valid ref + one non-existent question id (deleted/unknown question).
        List<MockInterviewQuestionRefDto> refs =
        [
            new MockInterviewQuestionRefDto(curated.Questions.SingleQuestionId), // valid
            new MockInterviewQuestionRefDto(Guid.NewGuid()),                     // deleted/unknown question
        ];
        await UpdateInterviewAsync(curated.MockInterviewId, questionsPerSession: null, refs);

        // Builder skips the dangling ref, keeps the valid one (no error).
        MockInterviewBuilderDto builder = await ReadResultAsync<MockInterviewBuilderDto>(
            await Client.GetAsync($"/trainer/mock-interviews/{curated.MockInterviewId}/builder"));
        MockInterviewBuilderQuestionDto only = Assert.Single(builder.Questions);
        Assert.Equal(curated.Questions.SingleQuestionId, only.QuestionId);

        // Student session also pools only the valid one.
        AuthenticateAs("platform-participant");
        SessionDto session = await StartInterviewSessionAsync(curated.MockInterviewId);
        SessionItemDto item = Assert.Single(session.Items);
        Assert.Equal(curated.Questions.SingleQuestionId, item.QuestionId);
    }

    [Fact]
    public async Task Publish_without_question_source_is_rejected()
    {
        AuthenticateAsAdmin();
        Guid mockId = await CreateInterviewAsync("empty-mock", topicIds: []);

        // No curated questions, no topics → publish must fail closed.
        HttpResponseMessage response = await Client.PostAsync($"/trainer/mock-interviews/{mockId}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.mock.interview.no.question.source", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Update_rejects_out_of_range_per_session()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 2);

        HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/trainer/mock-interviews/{curated.MockInterviewId}",
            new UpdateMockInterviewRequest("Найм", null, QuestionsPerSession: 0, Questions: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.mock.invalid_per_session", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Manage_list_includes_draft_with_curated_count()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: 2, publish: false);

        IReadOnlyList<MockInterviewManageItemDto> items = await ReadResultAsync<IReadOnlyList<MockInterviewManageItemDto>>(
            await Client.GetAsync("/trainer/mock-interviews/manage"));

        MockInterviewManageItemDto item = Assert.Single(items, m => m.Id == curated.MockInterviewId);
        Assert.False(item.IsPublished);
        Assert.Equal(4, item.QuestionCount);
        Assert.Equal(2, item.QuestionsPerSession);
    }

    [Fact]
    public async Task Summary_list_exposes_effective_question_count()
    {
        AuthenticateAsAdmin();
        await SeedCuratedInterviewAsync(questionsPerSession: 2);

        // Participant sees PUBLISHED interview with QuestionCount = min(perSession=2, curated=4) = 2.
        AuthenticateAs("platform-participant");
        IReadOnlyList<MockInterviewSummaryDto> items = await ReadResultAsync<IReadOnlyList<MockInterviewSummaryDto>>(
            await Client.GetAsync("/trainer/mock-interviews"));

        MockInterviewSummaryDto summary = Assert.Single(items);
        Assert.Equal(2, summary.QuestionCount);
    }

    [Fact]
    public async Task Summary_count_is_zero_when_all_curated_refs_dangle()
    {
        AuthenticateAsAdmin();
        Curated curated = await SeedCuratedInterviewAsync(questionsPerSession: null);

        // Re-point the whole curated set at non-existent question ids (deleted questions).
        List<MockInterviewQuestionRefDto> dangling =
        [
            new MockInterviewQuestionRefDto(Guid.NewGuid()),
            new MockInterviewQuestionRefDto(Guid.NewGuid()),
        ];
        await UpdateInterviewAsync(curated.MockInterviewId, questionsPerSession: null, dangling);

        // Dangling refs are skipped at start → the card must advertise 0 available, not the raw ref
        // count. The UI uses this to block the start button instead of erroring after the click (#568).
        AuthenticateAs("platform-participant");
        MockInterviewSummaryDto summary = Assert.Single(
            await ReadResultAsync<IReadOnlyList<MockInterviewSummaryDto>>(
                await Client.GetAsync("/trainer/mock-interviews")));
        Assert.Equal(0, summary.QuestionCount);
    }

    [Fact]
    public async Task Summary_count_reflects_legacy_topic_pool()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "legacy-mock-topic", "Тема");
        Guid bankId = await AddBankAsync(topicId, "FREE");
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        // Legacy topic-scope mock: no curated refs, just the topic. Pool = the topic's 4 questions —
        // the summary must report that (previously legacy always reported 0).
        Guid mockId = await CreateInterviewAsync("legacy-mock", topicIds: [topicId]);
        Assert.Equal(
            HttpStatusCode.OK,
            (await Client.PostAsync($"/trainer/mock-interviews/{mockId}/publish", null)).StatusCode);

        AuthenticateAs("platform-participant");
        MockInterviewSummaryDto summary = Assert.Single(
            await ReadResultAsync<IReadOnlyList<MockInterviewSummaryDto>>(
                await Client.GetAsync("/trainer/mock-interviews")));
        Assert.Equal(4, summary.QuestionCount);
    }

    // --- helpers ---

    private sealed record Curated(
        Guid MockInterviewId, Guid TrackId, Guid TopicId, Guid BankId,
        TrainerQuestionFixtures.SeededQuestions Questions)
    {
        public Guid[] QuestionIds =>
        [
            Questions.SingleQuestionId,
            Questions.MultiQuestionId,
            Questions.ExactQuestionId,
            Questions.OpenQuestionId,
        ];
    }

    /// <summary>
    ///     Seeds a track + published FREE topic with the canonical 4-question bank, then a mock-interview
    ///     curated with all 4 question refs and the given per-session draw size. Leaves the client admin.
    /// </summary>
    private async Task<Curated> SeedCuratedInterviewAsync(int? questionsPerSession, bool publish = true)
    {
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "mock-topic", "Тема собеса");
        Guid bankId = await AddBankAsync(topicId, "FREE");
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        Guid mockId = await CreateInterviewAsync("net-mock", topicIds: []);

        var curated = new Curated(mockId, trackId, topicId, bankId, questions);
        List<MockInterviewQuestionRefDto> refs =
            curated.QuestionIds.Select(qid => new MockInterviewQuestionRefDto(qid)).ToList();
        await UpdateInterviewAsync(mockId, questionsPerSession, refs);

        if (publish)
        {
            HttpResponseMessage publishResponse = await Client.PostAsync($"/trainer/mock-interviews/{mockId}/publish", null);
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        }

        return curated;
    }

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, "Runtime", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<Guid> AddBankAsync(Guid topicId, string tier)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicBankIdResponse>(response)).BankId;
    }

    private async Task<Guid> CreateInterviewAsync(string slug, IReadOnlyList<Guid> topicIds)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-interviews",
            new CreateMockInterviewRequest(slug, "Найм: мок-собес", null, topicIds, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<MockInterviewIdResponse>(response)).Id;
    }

    private async Task UpdateInterviewAsync(Guid mockId, int? questionsPerSession, IReadOnlyList<MockInterviewQuestionRefDto> refs)
    {
        HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/trainer/mock-interviews/{mockId}",
            new UpdateMockInterviewRequest("Найм: мок-собес", null, questionsPerSession, refs));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<SessionDto> StartInterviewSessionAsync(Guid mockId) =>
        await ReadResultAsync<SessionDto>(await StartRawAsync(mockId));

    private async Task<HttpResponseMessage> StartRawAsync(Guid mockId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/mock-interviews/{mockId}/sessions",
            new StartMockInterviewRequest(QuestionCount: null, TimeLimitSeconds: 1800));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }
}
