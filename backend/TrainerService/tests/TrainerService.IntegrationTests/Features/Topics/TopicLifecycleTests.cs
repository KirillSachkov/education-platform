using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Topics;

public sealed class TopicLifecycleTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Admin_creates_topic_adds_free_bank_publishes_succeeds()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        // 1a. Create topic → 200 with topic id.
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "async-await", "Async/await в .NET", "Runtime", "Описание", "BACKEND", null, null));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        TopicIdResponse created = await ReadResultAsync<TopicIdResponse>(createResponse);
        Assert.NotEqual(Guid.Empty, created.TopicId);

        // 1b. Add a FREE (empty) bank — questions are added separately via question CRUD (#623).
        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{created.TopicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Assert.Equal(HttpStatusCode.OK, bankResponse.StatusCode);
        TopicBankIdResponse bank = await ReadResultAsync<TopicBankIdResponse>(bankResponse);
        Assert.NotEqual(Guid.Empty, bank.BankId);

        // 1c. Publish → 200.
        HttpResponseMessage publishResponse =
            await Client.PostAsync($"/trainer/topics/{created.TopicId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
    }

    [Fact]
    public async Task GetTopics_returns_published_topic_with_mastery_defaults_and_free_bank_flag()
    {
        Guid topicId = await SeedPublishedTopicWithBankAsync("FREE");

        // 2. As a regular participant — published topic shows up with mastery defaults.
        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);

        TopicListItemDto topic = Assert.Single(topics, t => t.Id == topicId);
        Assert.Equal("Async/await в .NET", topic.Title);
        Assert.True(topic.HasFreeBank);
        Assert.False(topic.IsLocked);
        // Mastery defaults for a user who has never practised.
        Assert.Equal(0, topic.MasteryPercent);
        Assert.Equal(0, topic.AnswersCount);
        Assert.True(topic.IsWeak);
    }

    [Fact]
    public async Task GetTopics_hides_draft_topics()
    {
        // Topic created but NOT published.
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "draft-topic", "Черновик", "Runtime", null, null, null, null));
        TopicIdResponse created = await ReadResultAsync<TopicIdResponse>(createResponse);
        await Client.PostAsJsonAsync(
            $"/trainer/topics/{created.TopicId}/banks",
            new AddTopicBankRequest("FREE", null));

        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics");
        IReadOnlyList<TopicListItemDto> topics =
            await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(response);

        Assert.DoesNotContain(topics, t => t.Id == created.TopicId);
    }

    [Fact]
    public async Task AddBank_to_unknown_topic_returns_404()
    {
        // ECS is gone (#623) — AddBank no longer validates a quiz; the only NOT_FOUND path is a
        // missing topic. (Repurposed from the old "unknown quiz fails closed" ECS test.)
        AuthenticateAsAdmin();

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{Guid.NewGuid()}/banks",
            new AddTopicBankRequest("FREE", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("trainer.topic.not.found", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task NonAdmin_cannot_create_topic()
    {
        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(Guid.NewGuid(), "forbidden", "Нельзя", "Runtime", null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- helpers ---

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title, string area)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, area, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<Guid> SeedPublishedTopicWithBankAsync(string tier)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "async-await", "Async/await в .NET", "Runtime");

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }
}
