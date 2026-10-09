using CSharpFunctionalExtensions;
using FileService.Core.Features.AssetRegistry.IntegrationEvents;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;
using SharedKernel.Exceptions;

namespace FileService.IntegrationTests.Features.AssetRegistry;

/// <summary>
///     L1-тесты для Wolverine handler'а <c>BindMaterialDraftAssetsHandler</c>.
///     Регрессионная защита от блокера, который ловил code-review (#123):
///     batch slot-replace, отрабатывая на нефильтрованном списке драфтов
///     (включая PENDING_UPLOAD), сносил live предшественника даже когда draft
///     не bound'ился.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class BindMaterialDraftAssetsHandlerTests : FileServiceTestsBase
{
    public BindMaterialDraftAssetsHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PendingUploadDraft_ThrowsForRetryWithoutDeletingPredecessor()
    {
        // Arrange:
        // - материал X имеет live MATERIAL_PREVIEW (READY, bound)
        // - draft Y содержит PENDING_UPLOAD MATERIAL_PREVIEW (race: BindMaterialDraftAssets
        //   прилетел раньше, чем фронт сделал /files/{id}/complete)
        Guid materialId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();

        Guid livePreviewId = await SeedMaterialPreviewAsync(
            materialId,
            AssetStatus.READY,
            draftId: null,
            isTemporary: false,
            bindToMaterial: true);

        Guid pendingDraftId = await SeedMaterialPreviewAsync(
            materialId,
            AssetStatus.PENDING_UPLOAD,
            draftId: draftId,
            isTemporary: true,
            bindToMaterial: false,
            usageType: AssetUsageType.MARKDOWN_IMAGE);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        BindMaterialDraftAssetsHandler handler =
            ActivatorUtilities.CreateInstance<BindMaterialDraftAssetsHandler>(scope.ServiceProvider);

        await Assert.ThrowsAsync<TransientException>(() => handler.Handle(
            new BindMaterialDraftAssets(
                materialId,
                draftId.ToString(),
                ActorCanManageAnyAsset: true),
            CancellationToken.None));

        // Assert: live preview остался READY, draft не bound'нулся.
        await ExecuteInDb(async db =>
        {
            MediaAsset live = await db.MediaAssets.SingleAsync(a => a.Id == livePreviewId);
            MediaAsset pending = await db.MediaAssets.SingleAsync(a => a.Id == pendingDraftId);

            Assert.Equal(AssetStatus.READY, live.Status);
            Assert.NotNull(live.TargetEntity);
            Assert.Equal(materialId, live.TargetEntity.Id);

            Assert.Equal(AssetStatus.PENDING_UPLOAD, pending.Status);
            Assert.True(pending.IsTemporary);
            Assert.Equal(draftId, pending.DraftId);
        });
    }

    [Fact]
    public async Task ReadySingleSlotDraft_IsIgnoredBecauseCreateBindsExactSelection()
    {
        // The create payload already sync-binds the selected preview. Any remaining
        // preview in the shared draft is abandoned and must not replace it.
        Guid materialId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();

        Guid livePreviewId = await SeedMaterialPreviewAsync(
            materialId,
            AssetStatus.READY,
            draftId: null,
            isTemporary: false,
            bindToMaterial: true);

        Guid readyDraftId = await SeedMaterialPreviewAsync(
            materialId,
            AssetStatus.READY,
            draftId: draftId,
            isTemporary: true,
            bindToMaterial: false);

        await InvokeMessageAndWaitAsync(new BindMaterialDraftAssets(
            materialId,
            draftId.ToString(),
            ActorCanManageAnyAsset: true));

        await ExecuteInDb(async db =>
        {
            MediaAsset live = await db.MediaAssets.SingleAsync(a => a.Id == livePreviewId);
            MediaAsset abandoned = await db.MediaAssets.SingleAsync(a => a.Id == readyDraftId);

            Assert.Equal(AssetStatus.READY, live.Status);
            Assert.Equal(AssetStatus.READY, abandoned.Status);
            Assert.True(abandoned.IsTemporary);
            Assert.Equal(draftId, abandoned.DraftId);
            Assert.Null(abandoned.TargetEntity);
        });
    }

    [Fact]
    public async Task ReadyMaterialVideoDraft_IsIgnoredWithoutExplicitSelection()
    {
        Guid materialId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();
        Guid videoId = await SeedMaterialVideoAsync(draftId);
        OutboxCollector.Clear();

        await InvokeMessageAndWaitAsync(new BindMaterialDraftAssets(
            materialId,
            draftId.ToString(),
            ActorCanManageAnyAsset: true));

        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
        await ExecuteInDb(async db =>
        {
            MediaAsset video = await db.MediaAssets.SingleAsync(asset => asset.Id == videoId);
            Assert.True(video.IsTemporary);
            Assert.Equal(draftId, video.DraftId);
        });
    }

    [Fact]
    public async Task ForeignActorDraft_DoesNotBindAssets()
    {
        Guid materialId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        Guid foreignActorId = Guid.NewGuid();
        Guid assetId = await SeedMarkdownImageAsync(draftId, ownerId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        BindMaterialDraftAssetsHandler handler =
            ActivatorUtilities.CreateInstance<BindMaterialDraftAssetsHandler>(scope.ServiceProvider);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(
            new BindMaterialDraftAssets(materialId, draftId.ToString(), foreignActorId),
            CancellationToken.None));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(a => a.Id == assetId);
            Assert.True(asset.IsTemporary);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
        });
    }

    [Fact]
    public async Task DraftWithMoreThanOnePage_BindsEveryMarkdownAsset()
    {
        Guid materialId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        const int assetCount = 101;

        await ExecuteInDb(async db =>
        {
            for (int index = 0; index < assetCount; index++)
            {
                Guid assetId = Guid.CreateVersion7();
                MediaAsset asset = MediaAsset.Register(
                    assetId,
                    AssetKind.FILE,
                    AssetUsageType.MARKDOWN_IMAGE,
                    FileName.Of($"page-{index}-{assetId:N}.png").Value,
                    MediaContentType.Of("image/png").Value,
                    4,
                    draftId,
                    null,
                    true,
                    ownerId).Value;
                asset.MarkReady();
                db.MediaAssets.Add(asset);
                db.FileStorageRefs.Add(FileStorageRef.Create(
                    asset.Id,
                    StorageKey.ForFile(asset.Id, "png").Value).Value);
            }

            await db.SaveChangesAsync();
        });

        await InvokeMessageAndWaitAsync(new BindMaterialDraftAssets(
            materialId,
            draftId.ToString(),
            ownerId));

        await ExecuteInDb(async db =>
        {
            List<MediaAsset> assets = await db.MediaAssets
                .Where(asset => asset.TargetEntity != null && asset.TargetEntity.Id == materialId)
                .ToListAsync();
            Assert.Equal(assetCount, assets.Count);
            Assert.All(assets, asset => Assert.False(asset.IsTemporary));
        });
    }

    private async Task<Guid> SeedMarkdownImageAsync(Guid draftId, Guid ownerId)
    {
        Guid assetId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = MediaAsset.Register(
                assetId,
                AssetKind.FILE,
                AssetUsageType.MARKDOWN_IMAGE,
                FileName.Of($"image-{assetId:N}.png").Value,
                MediaContentType.Of("image/png").Value,
                4,
                draftId,
                null,
                true,
                ownerId).Value;
            asset.MarkReady();
            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(FileStorageRef.Create(
                asset.Id,
                StorageKey.ForFile(asset.Id, "png").Value).Value);
            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> SeedMaterialPreviewAsync(
        Guid materialId,
        AssetStatus status,
        Guid? draftId,
        bool isTemporary,
        bool bindToMaterial,
        AssetUsageType usageType = AssetUsageType.MATERIAL_PREVIEW)
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of("material", materialId);
            Result<FileName, Error> fileNameResult = FileName.Of($"preview-{Guid.NewGuid():N}.png");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("image/png");

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                usageType,
                fileNameResult.Value,
                contentTypeResult.Value,
                4,
                draftId,
                bindToMaterial ? targetEntityResult.Value : null,
                isTemporary);

            MediaAsset asset = assetResult.Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;

            if (status == AssetStatus.READY)
            {
                asset.MarkReady();

                if (isTemporary)
                {
                    // draft asset stays IsTemporary=true with DraftId — BindTo() will
                    // be called by the handler under test.
                }
            }

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;
            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> SeedMaterialVideoAsync(Guid draftId)
    {
        Guid assetId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = MediaAsset.Register(
                assetId,
                AssetKind.VIDEO,
                AssetUsageType.MATERIAL_VIDEO,
                FileName.Of($"video-{assetId:N}.mp4").Value,
                MediaContentType.Of("video/mp4").Value,
                1024,
                draftId,
                null,
                true,
                Guid.NewGuid()).Value;
            asset.MarkProcessing();
            asset.MarkReady();
            VideoProviderRef providerRef = VideoProviderRef.Create(
                assetId,
                AssetProviderType.KINESCOPE,
                $"kinescope-{assetId:N}").Value;

            db.MediaAssets.Add(asset);
            db.VideoProviderRefs.Add(providerRef);
            await db.SaveChangesAsync();
        });
        return assetId;
    }
}