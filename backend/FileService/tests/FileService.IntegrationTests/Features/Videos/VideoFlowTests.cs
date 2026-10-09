using System.Net;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services.AssetRegistry;
using FileService.Core.Services.Videos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Videos;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class VideoFlowTests : FileServiceTestsBase
{
    public VideoFlowTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task InitiateVideoUpload_CreatesProcessingVideoAsset()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", CreateVideoRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal("processing", envelope.Result.Status);
        Assert.NotEmpty(envelope.Result.ProviderVideoId);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == envelope.Result.AssetId);
            VideoProviderRef providerRef = await db.VideoProviderRefs.SingleAsync(x => x.AssetId == envelope.Result.AssetId);

            Assert.Equal(AssetKind.VIDEO, asset.Kind);
            Assert.Equal(AssetStatus.PROCESSING, asset.Status);
            Assert.Equal(envelope.Result.ProviderVideoId, providerRef.ExternalAssetId);
        });
    }

    [Fact]
    public async Task ReconcileVideos_UpdatesPendingVideoToReady()
    {
        InitiateVideoUploadResponse initiateResult = await InitiateVideoUploadAsync();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService service = scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();

        int changed = await service.ReconcileVideosAsync(CancellationToken.None);

        Assert.Equal(1, changed);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
        });
    }

    [Fact]
    public async Task ReconcileVideos_ConfirmsMaterialBindingWithoutProcessingEvents()
    {
        InitiateVideoUploadResponse initiateResult = await InitiateVideoUploadAsync();

        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService service = scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await service.ReconcileVideosAsync(CancellationToken.None);

        OutboxCollector.Clear();
        long revision = await ConfirmBindingAsync(initiateResult.AssetId);
        Assert.Empty(OutboxCollector.Messages);
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(revision, asset.ConfirmedBindingRevision);
        });
    }

    [Fact]
    public async Task ReconcileVideos_PublishesBinding_ForCourseVideo()
    {
        InitiateVideoUploadRequest request = new(
            FileName: "course-intro.mp4",
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "course_video",
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));
        HttpResponseMessage initiate = await AppHttpClient.PostAsJsonAsync("/videos/uploads", request);
        initiate.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService service = scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await service.ReconcileVideosAsync(CancellationToken.None);

        Assert.NotEmpty(OutboxCollector.OfType<FileAssetBound>());
    }

    [Fact]
    public async Task ReconcileVideos_ReplacementWaitsForAggregateConfirmation()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse first = await InitiateVideoForMaterialAsync(materialId, "first.mp4");

        await using (AsyncServiceScope firstScope = Services.CreateAsyncScope())
        {
            VideoReconciliationService firstReconciliation =
                firstScope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
            Assert.Equal(1, await firstReconciliation.ReconcileVideosAsync(CancellationToken.None));
        }
        long firstRevision = await ConfirmBindingAsync(first.AssetId);

        InitiateVideoUploadResponse second = await InitiateVideoForMaterialAsync(materialId, "second.mp4");
        OutboxCollector.Clear();

        await using AsyncServiceScope secondScope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            secondScope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        Assert.Equal(1, await reconciliation.ReconcileVideosAsync(CancellationToken.None));

        long secondRevision = await GetBindingRevisionAsync(second.AssetId);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(second.AssetId, secondRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(first.AssetId, firstRevision));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.DELETING,
                (await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId)).Status);
        });
        FileAssetDeleted deleted = Assert.Single(OutboxCollector.OfType<FileAssetDeleted>());
        Assert.Equal(first.AssetId, deleted.AssetId);
    }

    [Fact]
    public async Task ReconcileVideos_TwoPendingVideos_KeepsOnlyConfirmedSelection()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse first = await InitiateVideoForMaterialAsync(materialId, "first-pending.mp4");
        InitiateVideoUploadResponse second = await InitiateVideoForMaterialAsync(materialId, "second-pending.mp4");
        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        Assert.Equal(2, await reconciliation.ReconcileVideosAsync(CancellationToken.None));

        long firstRevision = await GetBindingRevisionAsync(first.AssetId);
        long secondRevision = await GetBindingRevisionAsync(second.AssetId);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(second.AssetId, secondRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(first.AssetId, firstRevision));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.DELETING,
                (await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId)).Status);
        });
        FileAssetDeleted deleted = Assert.Single(OutboxCollector.OfType<FileAssetDeleted>());
        Assert.Equal(first.AssetId, deleted.AssetId);
    }

    [Fact]
    public async Task ReconcileVideos_TwoPendingVideosFromDifferentAuthors_DoesNotChooseAuthorityItself()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadResponse victim =
            await InitiateVideoForMaterialAsync(materialId, "victim-pending.mp4");

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadResponse attacker =
            await InitiateVideoForMaterialAsync(materialId, "attacker-pending.mp4");
        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == victim.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == attacker.AssetId)).Status);
        });

        Assert.Empty(OutboxCollector.OfType<FileAssetDeleted>());
    }

    [Fact]
    public async Task ReconcileVideos_IgnoresDeletingCandidateWhenSelectingSlotWinner()
    {
        Guid materialId = Guid.NewGuid();
        Guid deletingProviderRefVersion = Guid.Empty;
        InitiateVideoUploadResponse active = await InitiateVideoForMaterialAsync(materialId, "active.mp4");
        InitiateVideoUploadResponse deleting = await InitiateVideoForMaterialAsync(materialId, "deleting.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == deleting.AssetId);
            asset.RequestDelete();
            deletingProviderRefVersion = (await db.VideoProviderRefs.SingleAsync(
                x => x.AssetId == deleting.AssetId)).Version;
            await db.SaveChangesAsync();
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == active.AssetId)).Status);
            Assert.Equal(
                AssetStatus.DELETING,
                (await db.MediaAssets.SingleAsync(x => x.Id == deleting.AssetId)).Status);
            Assert.Equal(
                deletingProviderRefVersion,
                (await db.VideoProviderRefs.SingleAsync(x => x.AssetId == deleting.AssetId)).Version);
        });
    }

    [Fact]
    public async Task ReconcileVideos_AuthorizedCrossAuthorReplacement_UsesAggregateConfirmation()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadResponse victim = await InitiateVideoForMaterialAsync(materialId, "victim.mp4");
        await using (AsyncServiceScope victimScope = Services.CreateAsyncScope())
        {
            VideoReconciliationService service =
                victimScope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
            await service.ReconcileVideosAsync(CancellationToken.None);
        }
        long victimRevision = await ConfirmBindingAsync(victim.AssetId);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateVideoUploadResponse attacker = await InitiateVideoForMaterialAsync(materialId, "attacker.mp4");
        OutboxCollector.Clear();
        await using AsyncServiceScope attackerScope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            attackerScope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);
        long attackerRevision = await GetBindingRevisionAsync(attacker.AssetId);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(attacker.AssetId, attackerRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(victim.AssetId, victimRevision));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.DELETING,
                (await db.MediaAssets.SingleAsync(x => x.Id == victim.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == attacker.AssetId)).Status);
        });
        FileAssetDeleted deleted = Assert.Single(OutboxCollector.OfType<FileAssetDeleted>());
        Assert.Equal(victim.AssetId, deleted.AssetId);
    }

    [Fact]
    public async Task ReconcileVideos_UnconfirmedCandidate_DoesNotRetireConfirmedLegacyVideo()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse older = await InitiateVideoForMaterialAsync(materialId, "older.mp4");
        InitiateVideoUploadResponse newer = await InitiateVideoForMaterialAsync(materialId, "newer.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset newerAsset = await db.MediaAssets.SingleAsync(x => x.Id == newer.AssetId);
            newerAsset.MarkProcessing();
            newerAsset.MarkReady();
            await db.SaveChangesAsync();
        });

        OutboxCollector.Clear();
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(older.AssetId, 0));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == older.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == newer.AssetId)).Status);

            MediaAsset olderAsset = await db.MediaAssets.SingleAsync(x => x.Id == older.AssetId);
            MediaAsset newerAsset = await db.MediaAssets.SingleAsync(x => x.Id == newer.AssetId);
            Assert.Equal(olderAsset.BindingRevision, olderAsset.ConfirmedBindingRevision);
            Assert.NotEqual(newerAsset.BindingRevision, newerAsset.ConfirmedBindingRevision);
        });
    }

    [Fact]
    public async Task ReconcileVideos_LowerConfirmedRevisionStillPublishesBinding()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse video =
            await InitiateVideoForMaterialAsync(materialId, "lower-confirmed.mp4");
        BindAssetRequest request = new(new TargetEntityDto("material", materialId));

        (await AppHttpClient.PostAsJsonAsync($"/files/{video.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long lowerRevision = await GetBindingRevisionAsync(video.AssetId);
        (await AppHttpClient.PostAsJsonAsync($"/files/{video.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long higherRevision = await GetBindingRevisionAsync(video.AssetId);
        Assert.True(higherRevision > lowerRevision);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(video.AssetId, lowerRevision));
        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);

        FileAssetBound bound = Assert.IsType<FileAssetBound>(Assert.Single(OutboxCollector.Messages));
        Assert.Equal(video.AssetId, bound.AssetId);
    }

    [Fact]
    public async Task ReconcileVideos_DetachedLowerRevisionPreservesDetachBoundary()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse video =
            await InitiateVideoForMaterialAsync(materialId, "detached-lower.mp4");
        BindAssetRequest request = new(new TargetEntityDto("material", materialId));

        (await AppHttpClient.PostAsJsonAsync($"/files/{video.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long lowerRevision = await GetBindingRevisionAsync(video.AssetId);
        (await AppHttpClient.PostAsJsonAsync($"/files/{video.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(video.AssetId, lowerRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(video.AssetId, lowerRevision));
        OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VideoReconciliationService reconciliation =
            scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
        await reconciliation.ReconcileVideosAsync(CancellationToken.None);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(item => item.Id == video.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(lowerRevision, asset.ConfirmedBindingRevision);
            Assert.Equal(lowerRevision, asset.DetachedThroughBindingRevision);
        });
    }

    [Fact]
    public async Task InitiateVideoUpload_PublishesVideoUploadInitiatedEvent()
    {
        Guid targetEntityId = Guid.NewGuid();
        InitiateVideoUploadRequest request = new(
            FileName: "event-test-video.mp4",
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", targetEntityId));

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        VideoUploadInitiated published = OutboxCollector.OfType<VideoUploadInitiated>().Single();
        Assert.Equal("video", published.Kind);
        Assert.Equal("material_video", published.UsageType);
        Assert.Equal(targetEntityId, published.TargetEntityId);
        Assert.Equal("material", published.TargetEntityType);
    }

    [Fact]
    public async Task DeleteVideo_MarksVideoDeleting_AndPublishesDeletedEvent()
    {
        InitiateVideoUploadResponse initiateResult = await InitiateVideoUploadAsync();

        // Phase 1: handler transitions to DELETING and publishes the outbox event.
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/videos/{initiateResult.AssetId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetDeleted published = OutboxCollector.OfType<FileAssetDeleted>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("video", published.Kind);
        Assert.Equal("material_video", published.UsageType);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });

        // Phase 2: retention sweep calls the Kinescope delete and finalizes the status.
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        int processed = await retention.ProcessDeletingAssetsAsync(CancellationToken.None);
        Assert.Equal(1, processed);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    private async Task<InitiateVideoUploadResponse> InitiateVideoUploadAsync()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", CreateVideoRequest());
        response.EnsureSuccessStatusCode();

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        return envelope!.Result!;
    }

    private async Task<InitiateVideoUploadResponse> InitiateVideoForMaterialAsync(
        Guid materialId,
        string fileName)
    {
        InitiateVideoUploadRequest request = new(
            FileName: fileName,
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", materialId));
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads/", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();
        return envelope!.Result!;
    }

    private async Task<long> GetBindingRevisionAsync(Guid assetId) =>
        await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == assetId)).BindingRevision);

    private async Task<long> ConfirmBindingAsync(Guid assetId)
    {
        long revision = await GetBindingRevisionAsync(assetId);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(assetId, revision));
        return revision;
    }

    private InitiateVideoUploadRequest CreateVideoRequest() =>
        new(
            FileName: "lesson-video.mp4",
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

}