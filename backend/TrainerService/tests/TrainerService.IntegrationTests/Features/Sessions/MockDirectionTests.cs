using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.Questions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     MOCK direction-scope (#568 Ф2): пул вопросов симуляции сужается по направлению трека.
///     Невалидное направление отвергается; пустое направление = весь трек.
/// </summary>
public sealed class MockDirectionTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task StartMock_with_direction_scopes_pool_to_that_direction()
    {
        Setup setup = await SeedTwoDirectionTrackAsync();
        AuthenticateAs("platform-participant");

        SessionDto session = await StartMockAsync(setup.TrackId, direction: "BACKEND");

        SessionItemDto item = Assert.Single(session.Items);
        Assert.Equal(setup.BackendTopic, item.TopicId); // only the BACKEND topic contributed
    }

    [Fact]
    public async Task StartMock_with_other_direction_scopes_to_frontend()
    {
        Setup setup = await SeedTwoDirectionTrackAsync();
        AuthenticateAs("platform-participant");

        SessionDto session = await StartMockAsync(setup.TrackId, direction: "FRONTEND");

        SessionItemDto item = Assert.Single(session.Items);
        Assert.Equal(setup.FrontendTopic, item.TopicId);
    }

    [Fact]
    public async Task StartMock_without_direction_pools_whole_track()
    {
        Setup setup = await SeedTwoDirectionTrackAsync();
        AuthenticateAs("platform-participant");

        SessionDto session = await StartMockAsync(setup.TrackId, direction: null);
        Assert.Equal(2, session.Items.Count); // both directions
    }

    [Fact]
    public async Task StartMock_with_invalid_direction_is_rejected()
    {
        Setup setup = await SeedTwoDirectionTrackAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(setup.TrackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null, Direction: "SIDEWAYS"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.topic.invalid.direction", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task StartMock_with_direction_having_no_topics_returns_no_questions()
    {
        Setup setup = await SeedTwoDirectionTrackAsync(); // only BACKEND + FRONTEND topics
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(setup.TrackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null, Direction: "FULLSTACK"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.mock.no.questions", await ReadErrorCodeAsync(response));
    }

    // --- helpers ---

    private sealed record Setup(Guid TrackId, Guid BackendTopic, Guid FrontendTopic);

    private async Task<SessionDto> StartMockAsync(Guid trackId, string? direction)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null, Direction: direction));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    /// <summary>Track with a BACKEND topic (1 question) and a FRONTEND topic (1 question). Leaves client admin.</summary>
    private async Task<Setup> SeedTwoDirectionTrackAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        Guid backendTopic = await CreateTopicAsync(trackId, "be-topic", "Бэкенд", "API", "BACKEND");
        Guid backendBank = await AddBankAsync(backendTopic);
        await TrainerQuestionFixtures.SeedSingleChoiceAsync(Factory, backendBank, "Вопрос бэкенда");
        await Client.PostAsync($"/trainer/topics/{backendTopic}/publish", null);

        Guid frontendTopic = await CreateTopicAsync(trackId, "fe-topic", "Фронтенд", "UI", "FRONTEND");
        Guid frontendBank = await AddBankAsync(frontendTopic);
        await TrainerQuestionFixtures.SeedSingleChoiceAsync(Factory, frontendBank, "Вопрос фронтенда");
        await Client.PostAsync($"/trainer/topics/{frontendTopic}/publish", null);

        return new Setup(trackId, backendTopic, frontendTopic);
    }

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title, string area, string direction)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, area, null, direction, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<Guid> AddBankAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicBankIdResponse>(response)).BankId;
    }
}
