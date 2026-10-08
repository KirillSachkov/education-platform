using System.Net;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services.Videos;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;
using System.Text.Json;

namespace FileService.IntegrationTests.Features.Videos;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class VideoQueryTests : FileServiceTestsBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public VideoQueryTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetVideo_ReturnsVideoWithMetadata()
    {
        Guid lessonId = Guid.NewGuid();
        Guid videoAssetId = await CreateVideoAsync(lessonId);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{videoAssetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetVideoResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal(videoAssetId, envelope.Result.Id);
        Assert.Equal("video", envelope.Result.Kind);
        Assert.Equal("material_video", envelope.Result.UsageType);
        Assert.NotNull(envelope.Result.ExternalVideoId);
    }

    [Fact]
    public async Task GetVideo_NonexistentId_ReturnsNullResult()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetVideoResponse?>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse?>>();

        Assert.NotNull(envelope);
        Assert.Null(envelope.Result);
    }

    [Fact]
    public async Task GetVideoByEntity_ReturnsVideoForEntity()
    {
        Guid lessonId = Guid.NewGuid();
        Guid videoAssetId = await CreateVideoAsync(lessonId);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(videoAssetId, 0));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/videos/by-entity?entityId={lessonId}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetVideoResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal(videoAssetId, envelope.Result.Id);
        Assert.Equal(lessonId, envelope.Result.TargetEntity!.Id);
    }

    [Fact]
    public async Task GetVideoByEntity_NoVideoForEntity_ReturnsNull()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/videos/by-entity?entityId={Guid.NewGuid()}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetVideoResponse?>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse?>>();

        Assert.NotNull(envelope);
        Assert.Null(envelope.Result);
    }

    [Fact]
    public async Task GetVideoByEntity_ExcludesDeletedVideos()
    {
        Guid lessonId = Guid.NewGuid();
        Guid videoAssetId = await CreateVideoAsync(lessonId);

        // Delete the video. Phase 1 transitions the asset to DELETING, which is
        // enough for the query to exclude it — no need to run the retention sweep.
        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync(
            $"/videos/{videoAssetId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/videos/by-entity?entityId={lessonId}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetVideoResponse?>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse?>>();

        Assert.NotNull(envelope);
        Assert.Null(envelope.Result);
    }

    [Fact]
    public async Task GetVideosBatch_AnonymousCallerIsDenied_ButInternalServiceCanRead()
    {
        Guid lessonId = Guid.NewGuid();
        Guid videoAssetId = await CreateVideoAsync(lessonId);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            VideoReconciliationService reconciliation =
                scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
            await reconciliation.ReconcileVideosAsync(CancellationToken.None);
        }

        Guid pendingVideoAssetId = await CreateVideoAsync(Guid.NewGuid());

        RemoveAuthentication();

        HttpResponseMessage anonymousResponse = await AppHttpClient.GetAsync(
            $"/internal/videos/batch/?ids={videoAssetId}&ids={pendingVideoAssetId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        AuthenticateAs(Guid.CreateVersion7(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/videos/batch/?ids={videoAssetId}&ids={pendingVideoAssetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Envelope<List<GetPublicVideoResponse>>? envelope =
            JsonSerializer.Deserialize<Envelope<List<GetPublicVideoResponse>>>(json, JsonOptions);
        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(videoAssetId, envelope.Result[0].Id);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement publicVideo = document.RootElement.GetProperty("result")[0];
        Assert.False(publicVideo.TryGetProperty("fileName", out _));
        Assert.False(publicVideo.TryGetProperty("status", out _));
        Assert.False(publicVideo.TryGetProperty("targetEntity", out _));
        Assert.False(publicVideo.TryGetProperty("externalVideoId", out _));
    }

    private async Task<Guid> CreateVideoAsync(Guid lessonId)
    {
        var request = new InitiateVideoUploadRequest(
            FileName: "lesson-video.mp4",
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", lessonId));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        return envelope!.Result!.AssetId;
    }
}
