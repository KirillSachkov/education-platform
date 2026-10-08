using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Contracts.Tracks;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Access;

/// <summary>
///     #614 F — анонимный (не залогиненный) посетитель может ПРОСМАТРИВАТЬ хаб тренажёра
///     read-only: треки, темы, список вопросов, мок-собесы. Любое ДЕЙСТВИЕ (старт сессии и т.п.)
///     требует входа. Проверяем: browse-эндпоинты отдают 200 анониму без персональных полей и без
///     ключей ответов; action-эндпоинт (старт сессии) анониму отдаёт 401.
/// </summary>
public sealed class AnonymousAccessTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task GetTracks_is_anonymous_and_returns_metadata_only()
    {
        await SeedPublishedFreeTopicAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/tracks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TrackDto> tracks = await ReadResultAsync<IReadOnlyList<TrackDto>>(response);
        TrackDto track = Assert.Single(tracks);
        Assert.Equal("dotnet", track.Slug);
        Assert.Equal(1, track.TopicCount); // one published topic seeded
    }

    [Fact]
    public async Task GetTopics_is_anonymous_with_no_personal_fields()
    {
        await SeedPublishedFreeTopicAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TopicListItemDto> topics = await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);
        TopicListItemDto topic = Assert.Single(topics);

        // Personal projections must be empty/default for an anonymous caller — no mastery leak.
        Assert.Equal(0, topic.MasteryPercent);
        Assert.Equal(0, topic.AnswersCount);
        Assert.True(topic.IsWeak); // default when there is no mastery row
        // FREE bank → not locked, anonymous browse allowed (SEO).
        Assert.True(topic.HasFreeBank);
        Assert.False(topic.IsLocked);
    }

    [Fact]
    public async Task GetQuestionList_is_anonymous_without_answer_keys()
    {
        Guid topicId = await SeedPublishedFreeTopicAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync($"/trainer/topics/{topicId}/questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        QuestionListDto list = await ReadResultAsync<QuestionListDto>(response);
        Assert.False(list.IsLocked);
        Assert.Equal(4, list.Items.Count);
        Assert.All(list.Items, i => Assert.Equal("NEW", i.Status)); // anon has no personal state

        // No-leak invariant: anonymous browse must not surface correct-answer fields / references.
        string raw = await ReadRawAsync(response);
        Assert.DoesNotContain("correctOptionIds", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceAnswer", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMockInterviews_is_anonymous_and_hides_drafts()
    {
        // Seed one PUBLISHED + one DRAFT named mock-interview; anon (IsAdmin=false) sees only PUBLISHED.
        await SeedMockInterviewAsync("mock-published", publish: true);
        await SeedMockInterviewAsync("mock-draft", publish: false);
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/mock-interviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<MockInterviewSummaryDto> interviews =
            await ReadResultAsync<IReadOnlyList<MockInterviewSummaryDto>>(response);
        MockInterviewSummaryDto only = Assert.Single(interviews);
        Assert.Equal("mock-published", only.Slug);
    }

    [Fact]
    public async Task StartSession_requires_authentication_for_anonymous()
    {
        Guid topicId = await SeedPublishedFreeTopicAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, 4));

        // Action endpoints stay gated behind Content.VIEW → no token = 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    /// <summary>Seeds a published topic + track with a FREE bank (canonical 4 local questions). Returns topic id.</summary>
    private async Task<Guid> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        await Client.PostAsync($"/trainer/tracks/{trackId}/publish", null); // GetTracks lists PUBLISHED only

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "study-topic", "Тема изучения", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }

    /// <summary>Seeds a named mock-interview (legacy topic-pool source). Optionally publishes it. Returns id.</summary>
    private async Task<Guid> SeedMockInterviewAsync(string slug, bool publish)
    {
        AuthenticateAsAdmin();
        Guid topicId = await SeedPublishedFreeTopicAsyncForMock();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/mock-interviews",
            new CreateMockInterviewRequest(slug, $"Симуляция {slug}", null, [topicId], null));
        Guid id = (await ReadResultAsync<MockInterviewIdResponse>(createResponse)).Id;

        if (publish)
            await Client.PostAsync($"/trainer/mock-interviews/{id}/publish", null);
        return id;
    }

    /// <summary>Per-mock unique published topic (mock-interview Publish() requires ≥1 question source).</summary>
    private async Task<Guid> SeedPublishedFreeTopicAsyncForMock()
    {
        Guid trackId = await CreateTrackAsync($"track-{Guid.NewGuid():N}", ".NET", "CSHARP");

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, $"topic-{Guid.NewGuid():N}", "Тема", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }
}
