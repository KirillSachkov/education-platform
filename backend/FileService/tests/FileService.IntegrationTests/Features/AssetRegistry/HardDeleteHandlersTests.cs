using Amazon.S3.Model;
using Core.Database;
using CSharpFunctionalExtensions;
using FileService.Core.Database;
using FileService.Core.FilesStorage;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class HardDeleteHandlersTests : FileServiceTestsBase
{
    public HardDeleteHandlersTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MaterialHardDeleted_RequestsDeletionForMaterialAssets()
    {
        Guid materialId = Guid.NewGuid();
        Guid fileId = await CreateFileAssetAsync(AssetUsageType.COURSE_PREVIEW, "course", Guid.NewGuid());
        Guid markdownId = await CreateFileAssetAsync(AssetUsageType.MARKDOWN_IMAGE, "material", materialId);
        Guid videoId = await CreateVideoAssetAsync(materialId);

        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(AssetStatus.READY, (await db.MediaAssets.SingleAsync(x => x.Id == fileId)).Status);
            Assert.Equal(AssetStatus.DELETING, (await db.MediaAssets.SingleAsync(x => x.Id == markdownId)).Status);
            Assert.Equal(AssetStatus.DELETING, (await db.MediaAssets.SingleAsync(x => x.Id == videoId)).Status);
        });
    }

    [Fact]
    public async Task IssueHardDeleted_RequestsDeletionForIssueMarkdownAssets()
    {
        Guid issueId = Guid.NewGuid();
        Guid fileId = await CreateFileAssetAsync(AssetUsageType.MARKDOWN_IMAGE, "issue", issueId);

        await InvokeMessageAndWaitAsync(new IssueHardDeleted(issueId));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(AssetStatus.DELETING, (await db.MediaAssets.SingleAsync(x => x.Id == fileId)).Status);
        });
    }

    [Fact]
    public async Task IssueHardDeleted_LeavesStoredObjectForRetentionSweep()
    {
        Guid issueId = Guid.NewGuid();
        Guid assetId = await CreateFileAssetAsync(AssetUsageType.MARKDOWN_IMAGE, "issue", issueId);
        string storageKey = StorageKey.ForFile(assetId, "png").Value.Value;
        await ExecuteInS3(async s3 =>
        {
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "media",
                Key = storageKey,
                InputStream = new MemoryStream([1, 2, 3, 4]),
                ContentType = "image/png",
            });
        });

        await InvokeMessageAndWaitAsync(new IssueHardDeleted(issueId));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == assetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });
        await ExecuteInS3(async s3 =>
        {
            GetObjectMetadataResponse metadata = await s3.GetObjectMetadataAsync("media", storageKey);
            Assert.Equal(4, metadata.ContentLength);
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        Assert.Equal(1, await retention.ProcessDeletingAssetsAsync(CancellationToken.None));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == assetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task DeleteByTargetEntity_SaveFailure_ThrowsForMessageRetry()
    {
        Guid issueId = Guid.NewGuid();
        await CreateFileAssetAsync(AssetUsageType.MARKDOWN_IMAGE, "issue", issueId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITransactionManager failingTransaction = Substitute.For<ITransactionManager>();
        failingTransaction.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));
        AssetDeletionLifecycleService service = new(
            scope.ServiceProvider.GetRequiredService<ILogger<AssetDeletionLifecycleService>>(),
            scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            failingTransaction);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteByTargetEntityAsync("issue", issueId, CancellationToken.None));
    }

    [Fact]
    public async Task ModuleHardDeleted_RequestsDeletionForMaterialAssets()
    {
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid materialVideoId = await CreateVideoAssetAsync(materialId);

        await InvokeMessageAndWaitAsync(new ModuleHardDeleted(moduleId, [materialId]));

        await ExecuteInDb(async db =>
        {
            Assert.Equal(AssetStatus.DELETING, (await db.MediaAssets.SingleAsync(x => x.Id == materialVideoId)).Status);
        });
    }

    [Fact]
    public async Task PurgeDeletedAssets_PurgesFilesButKeepsVideoOwnershipTombstone()
    {
        Guid materialId = Guid.NewGuid();
        Guid fileId = await CreateFileAssetAsync(AssetUsageType.MARKDOWN_IMAGE, "material", materialId);
        Guid videoId = await CreateVideoAssetAsync(materialId);

        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        Assert.Equal(2, await retention.ProcessDeletingAssetsAsync(CancellationToken.None));

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE media_assets
                 SET deleted_at = {DateTime.UtcNow.AddDays(-4)}
                 WHERE id = {fileId} OR id = {videoId}
                 """);
        });

        Assert.Equal(1, await retention.PurgeDeletedAssetsAsync(CancellationToken.None));

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.MediaAssets.AnyAsync(asset => asset.Id == fileId));
            Assert.True(await db.MediaAssets.AnyAsync(asset => asset.Id == videoId));
            Assert.True(await db.VideoProviderRefs.AnyAsync(providerRef => providerRef.AssetId == videoId));
        });
    }

    private async Task<Guid> CreateFileAssetAsync(
        AssetUsageType usageType,
        string entityType,
        Guid entityId,
        Guid? draftId = null)
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of(entityType, entityId);
            Result<FileName, Error> fileNameResult = FileName.Of($"asset-{Guid.NewGuid():N}.png");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("image/png");
            Guid? assetDraftId = usageType == AssetUsageType.MARKDOWN_IMAGE ? draftId ?? Guid.NewGuid() : draftId;
            TargetEntity? assetTargetEntity =
                usageType == AssetUsageType.MARKDOWN_IMAGE ? null : targetEntityResult.Value;

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                usageType,
                fileNameResult.Value,
                contentTypeResult.Value,
                4,
                assetDraftId,
                assetTargetEntity,
                usageType == AssetUsageType.MARKDOWN_IMAGE);

            MediaAsset asset = assetResult.Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;

            asset.MarkReady();

            if (usageType == AssetUsageType.MARKDOWN_IMAGE)
            {
                asset.BindTo(targetEntityResult.Value);
            }

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;
            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> CreateVideoAssetAsync(Guid materialId)
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of("material", materialId);
            Result<FileName, Error> fileNameResult = FileName.Of($"asset-{Guid.NewGuid():N}.mp4");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("video/mp4");

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.VIDEO,
                AssetUsageType.MATERIAL_VIDEO,
                fileNameResult.Value,
                contentTypeResult.Value,
                1024,
                null,
                targetEntityResult.Value,
                false);

            VideoProviderRef providerRef = VideoProviderRef.Create(
                assetResult.Value.Id,
                AssetProviderType.KINESCOPE,
                $"kinescope-{Guid.NewGuid():N}").Value;

            db.MediaAssets.Add(assetResult.Value);
            db.VideoProviderRefs.Add(providerRef);
            assetId = assetResult.Value.Id;
            await db.SaveChangesAsync();
        });

        return assetId;
    }
}
