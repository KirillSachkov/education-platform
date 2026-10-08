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
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Videos;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AttachExistingVideoTests : FileServiceTestsBase
{
    public AttachExistingVideoTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task AttachExistingVideo_ReadyVideo_CreatesReadyAssetAndPublishesBoundEvent()
    {
        // Arrange
        Guid lessonId = Guid.NewGuid();
        string externalVideoId = $"kinescope-video-{Guid.NewGuid():N}";

        var request = new AttachExistingVideoRequest(
            ExternalVideoId: externalVideoId,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", lessonId));

        // Act
        OutboxCollector.Clear();

        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<AttachExistingVideoResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<AttachExistingVideoResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal("ready", envelope.Result.Status);
        Assert.NotEqual(Guid.Empty, envelope.Result.AssetId);

        // Assert — FileAssetBound event was published
        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal("video", published.Kind);
        Assert.Equal("material_video", published.UsageType);
        Assert.Equal(lessonId, published.TargetEntityId);
        Assert.Equal("material", published.TargetEntityType);

        // Assert — asset and provider ref created in DB
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == published.AssetId);
            Assert.Equal(AssetKind.VIDEO, asset.Kind);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(AssetUsageType.MATERIAL_VIDEO, asset.UsageType);
            Assert.NotNull(asset.TargetEntity);
            Assert.Equal(lessonId, asset.TargetEntity.Id);
            Assert.NotNull(asset.CompletedAt);
            Assert.Equal(0, asset.Size);

            VideoProviderRef providerRef = await db.VideoProviderRefs.SingleAsync(x => x.AssetId == asset.Id);
            Assert.Equal(externalVideoId, providerRef.ExternalAssetId);
            Assert.Equal(AssetProviderType.KINESCOPE, providerRef.ProviderCode);
            Assert.NotNull(providerRef.Metadata);
        });
    }

    [Fact]
    public async Task AttachExistingVideo_ValidRequest_ReturnsMetadata()
    {
        // Arrange
        string externalVideoId = $"kinescope-video-{Guid.NewGuid():N}";

        var request = new AttachExistingVideoRequest(
            ExternalVideoId: externalVideoId,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        // Act
        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<AttachExistingVideoResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<AttachExistingVideoResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal("ready", envelope.Result.Status);
        Assert.NotNull(envelope.Result.ThumbnailUrl);
        Assert.NotNull(envelope.Result.DurationSeconds);
        Assert.Equal(120, envelope.Result.DurationSeconds);
    }

    [Fact]
    public async Task AttachExistingVideo_EmptyExternalVideoId_ReturnsBadRequest()
    {
        // Arrange
        var request = new AttachExistingVideoRequest(
            ExternalVideoId: string.Empty,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        // Act
        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_InvalidUsageType_ReturnsBadRequest()
    {
        // Arrange
        var request = new AttachExistingVideoRequest(
            ExternalVideoId: $"kinescope-video-{Guid.NewGuid():N}",
            UsageType: "avatar",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        // Act
        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_InvalidTargetEntityType_ReturnsBadRequest()
    {
        // Arrange
        var request = new AttachExistingVideoRequest(
            ExternalVideoId: $"kinescope-video-{Guid.NewGuid():N}",
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("unknown_type", Guid.NewGuid()));

        // Act
        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_UnauthorizedCrossAuthorSlotCollision_DoesNotDeleteVictimVideo()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAsAdmin();
        AttachExistingVideoRequest victimRequest = new(
            $"victim-{Guid.NewGuid():N}",
            "material_video",
            new TargetEntityDto("material", materialId));
        HttpResponseMessage victimResponse =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", victimRequest);
        victimResponse.EnsureSuccessStatusCode();
        Envelope<AttachExistingVideoResponse>? victimEnvelope =
            await victimResponse.Content.ReadFromJsonAsync<Envelope<AttachExistingVideoResponse>>();

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));
        AttachExistingVideoRequest attackerRequest = new(
            $"attacker-{Guid.NewGuid():N}",
            "material_video",
            new TargetEntityDto("material", materialId));
        HttpResponseMessage attackerResponse =
            await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", attackerRequest);

        Assert.Equal(HttpStatusCode.Forbidden, attackerResponse.StatusCode);
        await ExecuteInDb(async db =>
        {
            MediaAsset victim = await db.MediaAssets.SingleAsync(
                asset => asset.Id == victimEnvelope!.Result!.AssetId);
            Assert.Equal(AssetStatus.READY, victim.Status);
        });
    }

    [Fact]
    public async Task AttachExistingVideo_ConcurrentSameExternalId_CreatesSingleOwnershipRecord()
    {
        string externalVideoId = $"concurrent-{Guid.NewGuid():N}";
        var request = new AttachExistingVideoRequest(
            externalVideoId,
            "material_video",
            TargetEntity: null,
            DraftId: Guid.NewGuid());

        HttpResponseMessage[] responses = await Task.WhenAll(
            AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", request),
            AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", request));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                1,
                await db.VideoProviderRefs.CountAsync(
                    providerRef => providerRef.ExternalAssetId == externalVideoId));
        });
    }

    [Fact]
    public async Task AttachExistingVideo_ProviderAliasResolvingToOwnedCanonicalId_Returns409()
    {
        string canonicalId = Guid.NewGuid().ToString("D");
        string alias = $"alias-{Guid.NewGuid():N}";
        AuthenticateAsAdmin();
        AttachExistingVideoRequest first = new(
            canonicalId,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));
        (await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", first))
            .EnsureSuccessStatusCode();

        IVideoProvider provider = Services.GetRequiredService<IVideoProvider>();
        provider.GetStatusAsync(alias, Arg.Any<CancellationToken>())
            .Returns(Result.Success<VideoProviderAssetInfo, Error>(new VideoProviderAssetInfo(
                canonicalId,
                "Canonical video",
                "done",
                null,
                120,
                1920,
                1080)));
        AuthenticateAsAdmin();
        AttachExistingVideoRequest second = new(
            alias,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/videos/attach-existing/",
            second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_AfterDetach_AllowsImmediateReuse()
    {
        string externalVideoId = $"reattach-{Guid.NewGuid():N}";
        Guid materialId = Guid.NewGuid();
        var request = new AttachExistingVideoRequest(
            externalVideoId,
            "material_video",
            new TargetEntityDto("material", materialId));

        HttpResponseMessage firstResponse = await AppHttpClient.PostAsJsonAsync(
            "/videos/attach-existing/",
            request);
        firstResponse.EnsureSuccessStatusCode();
        Envelope<AttachExistingVideoResponse>? firstEnvelope =
            await firstResponse.Content.ReadFromJsonAsync<Envelope<AttachExistingVideoResponse>>();
        Guid detachedAssetId = firstEnvelope!.Result!.AssetId;

        HttpResponseMessage detachResponse = await AppHttpClient.PostAsync(
            $"/files/{detachedAssetId}/detach/",
            content: null);
        Assert.Equal(HttpStatusCode.OK, detachResponse.StatusCode);

        HttpResponseMessage secondResponse = await AppHttpClient.PostAsJsonAsync(
            "/videos/attach-existing/",
            request);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Envelope<AttachExistingVideoResponse>? secondEnvelope =
            await secondResponse.Content.ReadFromJsonAsync<Envelope<AttachExistingVideoResponse>>();
        Assert.NotNull(secondEnvelope?.Result);
        Assert.NotEqual(detachedAssetId, secondEnvelope.Result.AssetId);
        Assert.Equal("ready", secondEnvelope.Result.Status);

        await ExecuteInDb(async db =>
        {
            MediaAsset detachedAsset = await db.MediaAssets.SingleAsync(x => x.Id == detachedAssetId);
            Assert.Equal(AssetStatus.DELETING, detachedAsset.Status);

            VideoProviderRef activeProviderRef = await db.VideoProviderRefs.SingleAsync(
                x => x.ExternalAssetId == externalVideoId);
            Assert.Equal(secondEnvelope.Result.AssetId, activeProviderRef.AssetId);
        });
    }

    [Fact]
    public async Task AttachExistingVideo_WhenExternalIdIsActive_ReturnsConflict()
    {
        string externalVideoId = $"active-{Guid.NewGuid():N}";
        var request = new AttachExistingVideoRequest(
            externalVideoId,
            "material_video",
            new TargetEntityDto("material", Guid.NewGuid()));

        (await AppHttpClient.PostAsJsonAsync("/videos/attach-existing/", request))
            .EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await AppHttpClient.PostAsJsonAsync(
            "/videos/attach-existing/",
            request);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        string responseBody = await duplicateResponse.Content.ReadAsStringAsync();
        Assert.Contains("video.external.already.attached", responseBody, StringComparison.Ordinal);
        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                1,
                await db.VideoProviderRefs.CountAsync(x => x.ExternalAssetId == externalVideoId));
        });
    }

}