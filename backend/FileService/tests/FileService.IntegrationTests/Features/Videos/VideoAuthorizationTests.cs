using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Videos;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class VideoAuthorizationTests : FileServiceTestsBase
{
    public VideoAuthorizationTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task AttachExistingVideo_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        AttachExistingVideoRequest request = new(
            ExternalVideoId: "test-provider-id",
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_NonAdminUser_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        AttachExistingVideoRequest request = new(
            ExternalVideoId: "test-provider-id",
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteVideo_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/videos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteVideo_FormerUploaderWithoutCurrentTargetAccess_Returns403()
    {
        Guid ownerId = Guid.NewGuid();
        Guid assetId = await SeedProviderVideoAsync(ownerId, $"former-{Guid.NewGuid():N}");
        AuthenticateAs(ownerId, "platform-author");
        TargetEntityAuthorization
            .AuthorizeManagerAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/videos/{assetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == assetId)).Status));
    }

    [Fact]
    public async Task GetVideo_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ForeignAuthorIsDenied_ButInternalServiceCanReadReadyVideo()
    {
        Guid ownerId = Guid.NewGuid();
        Guid assetId = await SeedProviderVideoAsync(ownerId, $"private-{Guid.NewGuid():N}");

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage publicResponse = await AppHttpClient.GetAsync($"/videos/{assetId}");
        Assert.Equal(HttpStatusCode.Forbidden, publicResponse.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage internalResponse = await AppHttpClient.GetAsync($"/internal/videos/{assetId}/");
        Assert.Equal(HttpStatusCode.OK, internalResponse.StatusCode);
        Envelope<GetVideoResponse?>? envelope =
            await internalResponse.Content.ReadFromJsonAsync<Envelope<GetVideoResponse?>>();
        Assert.NotNull(envelope?.Result?.ExternalVideoId);
    }

    [Fact]
    public async Task GetVideo_InternalServiceCanReadBoundProcessingVideo()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadRequest request = new(
            "processing.mp4",
            "video/mp4",
            1024,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));
        HttpResponseMessage initiateResponse = await AppHttpClient.PostAsJsonAsync(
            "/videos/uploads/",
            request);
        initiateResponse.EnsureSuccessStatusCode();
        Envelope<InitiateVideoUploadResponse>? initiate =
            await initiateResponse.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/videos/{initiate!.Result!.AssetId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<GetVideoResponse?>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetVideoResponse?>>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal("processing", envelope.Result.Status);
    }

    [Fact]
    public async Task GetVideoByEntity_NonAdminUser_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/videos/by-entity?entityId={Guid.NewGuid()}&entityType=material");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceChapters_NonOwnerAuthor_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadRequest initiateRequest = new(
            FileName: "owner-video.mp4",
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage initiateResponse =
            await AppHttpClient.PostAsJsonAsync("/videos/uploads/", initiateRequest);
        initiateResponse.EnsureSuccessStatusCode();
        Envelope<InitiateVideoUploadResponse>? envelope =
            await initiateResponse.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        ReplaceVideoChaptersRequest request = new(
        [
            new ReplaceVideoChapterItemDto(null, "Чужая глава", 0, 0),
        ]);

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/videos/{envelope!.Result!.AssetId}/chapters/",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetChapters_NonOwnerAuthor_Returns403WithoutProviderCall()
    {
        Guid ownerId = Guid.CreateVersion7();
        Guid assetId = await SeedProviderVideoAsync(ownerId, $"chapters-{Guid.CreateVersion7():N}");
        IVideoProvider provider = Services.GetRequiredService<IVideoProvider>();
        provider.ClearReceivedCalls();
        AuthenticateAs(Guid.CreateVersion7(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{assetId}/chapters/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await provider.DidNotReceive().GetChaptersAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetChapters_InternalServiceCanReadForeignVideo()
    {
        string externalId = $"service-chapters-{Guid.CreateVersion7():N}";
        Guid assetId = await SeedProviderVideoAsync(Guid.CreateVersion7(), externalId);
        IVideoProvider provider = Services.GetRequiredService<IVideoProvider>();
        provider.GetChaptersAsync(externalId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<VideoProviderChapter>, Error>([]));
        AuthenticateAs(Guid.Empty, "platform-service");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{assetId}/chapters/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_ExternalIdAlreadyAttached_Returns409()
    {
        string externalVideoId = $"shared-{Guid.NewGuid():N}";
        AuthenticateAsAdmin();
        AttachExistingVideoRequest first = new(
            externalVideoId,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));
        (await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", first))
            .EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        AttachExistingVideoRequest duplicate = new(
            externalVideoId,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", duplicate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceChapters_DuplicateProviderIdOwnedByAnotherUser_Returns403()
    {
        Guid victimOwnerId = Guid.NewGuid();
        Guid attackerId = Guid.NewGuid();
        string externalVideoId = $"duplicate-{Guid.NewGuid():N}";
        Guid attackerAssetId = await SeedProviderVideoAsync(attackerId, externalVideoId);
        await SeedProviderVideoAsync(victimOwnerId, externalVideoId);
        AuthenticateAs(attackerId, "platform-author");

        ReplaceVideoChaptersRequest request = new(
        [
            new ReplaceVideoChapterItemDto(null, "Атака", 0, 0),
        ]);
        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/videos/{attackerAssetId}/chapters/",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedProviderVideoAsync(Guid ownerId, string externalVideoId)
    {
        Guid assetId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            Result<FileName, Error> fileName = FileName.Of($"{assetId:N}.mp4");
            Result<MediaContentType, Error> contentType = MediaContentType.Of("video/mp4");
            Result<TargetEntity, Error> target = TargetEntity.Of("material", Guid.NewGuid());
            MediaAsset asset = MediaAsset.RegisterFromProvider(
                assetId,
                AssetUsageType.MATERIAL_VIDEO,
                fileName.Value,
                contentType.Value,
                target.Value,
                AssetStatus.READY,
                null,
                ownerId).Value;
            VideoProviderRef providerRef = VideoProviderRef.Create(
                assetId,
                AssetProviderType.KINESCOPE,
                externalVideoId).Value;

            db.MediaAssets.Add(asset);
            db.VideoProviderRefs.Add(providerRef);
            await db.SaveChangesAsync();
        });

        return assetId;
    }
}
