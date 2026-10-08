using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Topics;
using TrainerService.Contracts.Tracks;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Tracks;

/// <summary>
///     Track CRUD/publish (admin/seed) + the student-facing GetTracks projection and the
///     ?trackId / ?direction filters on GetTopics — none of which had coverage before #568.
/// </summary>
public sealed class TrackLifecycleTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Admin_creates_updates_publishes_track_and_it_shows_in_GetTracks()
    {
        AuthenticateAsAdmin();

        // Create as DRAFT.
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/tracks",
            new CreateTrackRequest("dotnet", ".NET", "CSHARP", "Бэкенд на C#"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid trackId = (await ReadResultAsync<TrackIdResponse>(createResponse)).TrackId;
        Assert.NotEqual(Guid.Empty, trackId);

        // Not published yet → absent from the student tracks list.
        AuthenticateAs("platform-participant");
        IReadOnlyList<TrackDto> beforePublish =
            await ReadResultAsync<IReadOnlyList<TrackDto>>(await Client.GetAsync("/trainer/tracks"));
        Assert.DoesNotContain(beforePublish, t => t.Id == trackId);

        // Update details.
        AuthenticateAsAdmin();
        HttpResponseMessage updateResponse = await Client.PutAsJsonAsync(
            $"/trainer/tracks/{trackId}",
            new UpdateTrackRequest(".NET / C#", "CSHARP", "Обновлённое описание"));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Publish.
        HttpResponseMessage publishResponse = await Client.PostAsync($"/trainer/tracks/{trackId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        // Now visible to a participant with the updated title.
        AuthenticateAs("platform-participant");
        IReadOnlyList<TrackDto> afterPublish =
            await ReadResultAsync<IReadOnlyList<TrackDto>>(await Client.GetAsync("/trainer/tracks"));
        TrackDto track = Assert.Single(afterPublish, t => t.Id == trackId);
        Assert.Equal(".NET / C#", track.Title);
        Assert.Equal("CSHARP", track.Stack);
        Assert.Equal(0, track.TopicCount); // no published topics yet
    }

    [Fact]
    public async Task GetTracks_counts_only_published_topics()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        await Client.PostAsync($"/trainer/tracks/{trackId}/publish", null);

        // One published topic + one draft topic under the same track.
        Guid publishedTopic = await CreateTopicAsync(trackId, "pub", "Опубликована", "Runtime");
        await AddFreeBankWithQuestionsAsync(publishedTopic);
        await Client.PostAsync($"/trainer/topics/{publishedTopic}/publish", null);

        await CreateTopicAsync(trackId, "draft", "Черновик", "Runtime");

        AuthenticateAs("platform-participant");
        IReadOnlyList<TrackDto> tracks =
            await ReadResultAsync<IReadOnlyList<TrackDto>>(await Client.GetAsync("/trainer/tracks"));

        TrackDto track = Assert.Single(tracks, t => t.Id == trackId);
        Assert.Equal(1, track.TopicCount); // draft topic not counted
    }

    [Fact]
    public async Task CreateTrack_with_duplicate_slug_conflicts()
    {
        AuthenticateAsAdmin();
        await CreateTrackAsync("dotnet", ".NET", "CSHARP");

        HttpResponseMessage dup = await Client.PostAsJsonAsync(
            "/trainer/tracks",
            new CreateTrackRequest("dotnet", "Другой .NET", "CSHARP", null));

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("trainer.track.slug.already.exists", await ReadErrorCodeAsync(dup));
    }

    [Fact]
    public async Task CreateTrack_with_invalid_stack_is_rejected()
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/tracks",
            new CreateTrackRequest("rust", "Rust", "RUST", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.track.invalid.stack", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task NonAdmin_cannot_create_track()
    {
        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/tracks",
            new CreateTrackRequest("forbidden", "Нельзя", "CSHARP", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTopics_filters_by_trackId_and_direction()
    {
        AuthenticateAsAdmin();

        // Two tracks, each with one published topic; one topic is BACKEND, the other FRONTEND.
        Guid backendTrack = await CreateTrackAsync("dotnet", ".NET", "CSHARP");
        Guid frontendTrack = await CreateTrackAsync("ts", "TypeScript", "TYPESCRIPT");

        Guid backendTopic = await CreatePublishedTopicAsync(backendTrack, "be-async", "Async", "Runtime", "BACKEND");
        Guid frontendTopic = await CreatePublishedTopicAsync(frontendTrack, "fe-react", "React", "UI", "FRONTEND");

        AuthenticateAs("platform-participant");

        // Filter by backend track → only its topic.
        IReadOnlyList<TopicListItemDto> backendOnly = await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(
            await Client.GetAsync($"/trainer/topics?trackId={backendTrack}"));
        Assert.Single(backendOnly);
        Assert.Equal(backendTopic, backendOnly[0].Id);

        // Filter by direction=FRONTEND across all tracks → only the frontend topic.
        IReadOnlyList<TopicListItemDto> frontendOnly = await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(
            await Client.GetAsync("/trainer/topics?direction=FRONTEND"));
        Assert.Single(frontendOnly);
        Assert.Equal(frontendTopic, frontendOnly[0].Id);

        // Mismatched track+direction combo → empty.
        IReadOnlyList<TopicListItemDto> none = await ReadResultAsync<IReadOnlyList<TopicListItemDto>>(
            await Client.GetAsync($"/trainer/topics?trackId={backendTrack}&direction=FRONTEND"));
        Assert.Empty(none);
    }

    [Fact]
    public async Task GetTopics_with_invalid_direction_is_rejected()
    {
        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.GetAsync("/trainer/topics?direction=SIDEWAYS");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.topic.invalid.direction", await ReadErrorCodeAsync(response));
    }

    // --- helpers ---

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title, string area, string? direction = null)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, area, null, direction, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task AddFreeBankWithQuestionsAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(response)).BankId;
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
    }

    private async Task<Guid> CreatePublishedTopicAsync(
        Guid trackId, string slug, string title, string area, string direction)
    {
        Guid topicId = await CreateTopicAsync(trackId, slug, title, area, direction);
        await AddFreeBankWithQuestionsAsync(topicId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }
}
