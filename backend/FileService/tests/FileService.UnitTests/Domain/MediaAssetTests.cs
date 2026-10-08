using FileService.Domain;

namespace FileService.UnitTests.Domain;

public class MediaAssetTests
{
    [Fact]
    public void PendingUpload_MarkProcessing_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD);

        var result = asset.MarkProcessing();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.PROCESSING, asset.Status);
    }

    [Fact]
    public void PendingUpload_MarkReady_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD);

        var result = asset.MarkReady();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.READY, asset.Status);
    }

    [Fact]
    public void PendingUpload_MarkFailed_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD);

        var result = asset.MarkFailed("upload timed out");

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.FAILED, asset.Status);
        Assert.Equal("upload timed out", asset.FailureReason);
    }

    [Fact]
    public void PendingUpload_RequestDelete_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD);

        var result = asset.RequestDelete();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETING, asset.Status);
    }

    [Fact]
    public void Processing_MarkProcessing_IsIdempotent()
    {
        var asset = CreateAssetInStatus(AssetStatus.PROCESSING);

        var result = asset.MarkProcessing();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.PROCESSING, asset.Status);
    }

    [Fact]
    public void Processing_MarkReady_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PROCESSING);

        var result = asset.MarkReady();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.READY, asset.Status);
    }

    [Fact]
    public void Processing_MarkFailed_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PROCESSING);

        var result = asset.MarkFailed("processing error");

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.FAILED, asset.Status);
        Assert.Equal("processing error", asset.FailureReason);
    }

    [Fact]
    public void Processing_RequestDelete_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.PROCESSING);

        var result = asset.RequestDelete();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETING, asset.Status);
    }

    [Fact]
    public void Ready_MarkProcessing_Fails()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        var result = asset.MarkProcessing();

        Assert.True(result.IsFailure);
        Assert.Equal(AssetStatus.READY, asset.Status);
    }

    [Fact]
    public void Ready_MarkReady_IsIdempotent()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        var result = asset.MarkReady();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.READY, asset.Status);
    }

    [Fact]
    public void Ready_MarkFailed_Fails()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        var result = asset.MarkFailed("should not work");

        Assert.True(result.IsFailure);
        Assert.Equal(AssetStatus.READY, asset.Status);
    }

    [Fact]
    public void Ready_RequestDelete_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        var result = asset.RequestDelete();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETING, asset.Status);
    }

    [Fact]
    public void Failed_RequestDelete_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.FAILED);

        var result = asset.RequestDelete();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETING, asset.Status);
    }

    [Fact]
    public void Failed_MarkReady_Fails()
    {
        var asset = CreateAssetInStatus(AssetStatus.FAILED);

        var result = asset.MarkReady();

        Assert.True(result.IsFailure);
        Assert.Equal(AssetStatus.FAILED, asset.Status);
    }

    [Fact]
    public void Deleting_MarkDeleted_Succeeds()
    {
        var asset = CreateAssetInStatus(AssetStatus.DELETING);

        var result = asset.MarkDeleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETED, asset.Status);
    }

    [Fact]
    public void Deleting_RequestDelete_IsIdempotent()
    {
        var asset = CreateAssetInStatus(AssetStatus.DELETING);

        var result = asset.RequestDelete();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETING, asset.Status);
    }

    [Fact]
    public void Deleted_MarkDeleted_IsIdempotent()
    {
        var asset = CreateAssetInStatus(AssetStatus.DELETED);

        var result = asset.MarkDeleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetStatus.DELETED, asset.Status);
    }

    [Fact]
    public void Register_Avatar_EntityRequired_Succeeds()
    {
        var target = ValidTargetEntity("user");

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.AVATAR,
            ValidFileName("avatar.jpg"),
            ValidContentType("image/jpeg"),
            1024,
            draftId: null,
            targetEntity: target,
            isTemporary: false);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(AssetStatus.PENDING_UPLOAD, asset.Status);
        Assert.Equal(AssetKind.FILE, asset.Kind);
        Assert.Equal(AssetUsageType.AVATAR, asset.UsageType);
        Assert.Equal(target, asset.TargetEntity);
        Assert.Null(asset.DraftId);
        Assert.False(asset.IsTemporary);
    }

    [Fact]
    public void Register_MarkdownImage_WithDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.MARKDOWN_IMAGE,
            ValidFileName("image.png"),
            ValidContentType("image/png"),
            2048,
            draftId: draftId,
            targetEntity: null,
            isTemporary: true);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(draftId, asset.DraftId);
        Assert.Null(asset.TargetEntity);
        Assert.True(asset.IsTemporary);
    }

    [Fact]
    public void Register_MarkdownImage_WithEntity_Succeeds()
    {
        var target = ValidTargetEntity("material");

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.MARKDOWN_IMAGE,
            ValidFileName("image.webp"),
            ValidContentType("image/webp"),
            4096,
            draftId: null,
            targetEntity: target,
            isTemporary: false);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(target, asset.TargetEntity);
        Assert.Null(asset.DraftId);
        Assert.False(asset.IsTemporary);
    }

    [Fact]
    public void Register_KindMismatch_VideoKindWithAvatarUsage_Fails()
    {
        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.VIDEO,
            AssetUsageType.AVATAR,
            ValidFileName("avatar.jpg"),
            ValidContentType("image/jpeg"),
            1024,
            draftId: null,
            targetEntity: ValidTargetEntity("user"),
            isTemporary: false);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_SizeOverLimit_Fails()
    {
        long overLimit = 6 * 1024 * 1024;

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.AVATAR,
            ValidFileName("avatar.jpg"),
            ValidContentType("image/jpeg"),
            overLimit,
            draftId: null,
            targetEntity: ValidTargetEntity("user"),
            isTemporary: false);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_WrongMimeType_Fails()
    {
        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.AVATAR,
            ValidFileName("avatar.pdf"),
            ValidContentType("application/pdf"),
            1024,
            draftId: null,
            targetEntity: ValidTargetEntity("user"),
            isTemporary: false);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_BothDraftAndTarget_Fails()
    {
        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.MARKDOWN_IMAGE,
            ValidFileName("image.jpg"),
            ValidContentType("image/jpeg"),
            1024,
            draftId: Guid.CreateVersion7(),
            targetEntity: ValidTargetEntity("material"),
            isTemporary: true);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_EntityRequired_WithDraft_Fails()
    {
        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.AVATAR,
            ValidFileName("avatar.jpg"),
            ValidContentType("image/jpeg"),
            1024,
            draftId: Guid.CreateVersion7(),
            targetEntity: null,
            isTemporary: true);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_DraftOrEntity_NeitherProvided_Fails()
    {
        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.MARKDOWN_IMAGE,
            ValidFileName("image.jpg"),
            ValidContentType("image/jpeg"),
            1024,
            draftId: null,
            targetEntity: null,
            isTemporary: false);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void RegisterFromProvider_ReadyStatus_Succeeds()
    {
        var target = ValidTargetEntity("material");

        var result = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            AssetUsageType.MATERIAL_VIDEO,
            ValidFileName("video.mp4"),
            ValidContentType("video/mp4"),
            target,
            AssetStatus.READY);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(AssetStatus.READY, asset.Status);
        Assert.Equal(AssetKind.VIDEO, asset.Kind);
        Assert.NotNull(asset.CompletedAt);
    }

    [Fact]
    public void RegisterFromProvider_ProcessingStatus_Succeeds()
    {
        var target = ValidTargetEntity("material");

        var result = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            AssetUsageType.MATERIAL_VIDEO,
            ValidFileName("video.mp4"),
            ValidContentType("video/mp4"),
            target,
            AssetStatus.PROCESSING);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(AssetStatus.PROCESSING, asset.Status);
        Assert.NotNull(asset.ProcessingStartedAt);
    }

    [Fact]
    public void RegisterFromProvider_FailedStatus_Fails()
    {
        var target = ValidTargetEntity("material");

        var result = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            AssetUsageType.MATERIAL_VIDEO,
            ValidFileName("video.mp4"),
            ValidContentType("video/mp4"),
            target,
            AssetStatus.FAILED);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void RegisterFromProvider_NonVideoKind_Fails()
    {
        var target = ValidTargetEntity("user");

        var result = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            AssetUsageType.AVATAR,
            ValidFileName("avatar.jpg"),
            ValidContentType("image/jpeg"),
            target,
            AssetStatus.READY);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BindTo_ReadyTemporaryDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            draftId: draftId,
            isTemporary: true);
        asset.MarkReady();

        var target = ValidTargetEntity("material");
        var result = asset.BindTo(target);

        Assert.True(result.IsSuccess);
        Assert.Equal(target, asset.TargetEntity);
        Assert.Null(asset.DraftId);
        Assert.False(asset.IsTemporary);
        Assert.NotNull(asset.BoundAt);
    }

    [Fact]
    public void BindTo_NotReady_Fails()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            draftId: draftId,
            isTemporary: true);

        var target = ValidTargetEntity("material");
        var result = asset.BindTo(target);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BindTo_ReadyButNotTemporary_Fails()
    {
        var asset = CreateAsset(
            usageType: AssetUsageType.AVATAR,
            targetEntity: ValidTargetEntity("user"),
            isTemporary: false);
        asset.MarkReady();

        var newTarget = ValidTargetEntity("user");
        var result = asset.BindTo(newTarget);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BindTo_InvalidTargetType_Fails()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            draftId: draftId,
            isTemporary: true);
        asset.MarkReady();

        var invalidTarget = ValidTargetEntity("user");
        var result = asset.BindTo(invalidTarget);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BindTo_ProcessingVideoDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MATERIAL_VIDEO,
            draftId: draftId,
            isTemporary: true);
        asset.MarkProcessing();

        var target = ValidTargetEntity("material");
        var result = asset.BindTo(target);

        Assert.True(result.IsSuccess);
        Assert.Equal(target, asset.TargetEntity);
        Assert.Null(asset.DraftId);
        Assert.False(asset.IsTemporary);
        Assert.Equal(AssetStatus.PROCESSING, asset.Status);
    }

    [Fact]
    public void BindTo_ProcessingFileDraft_Fails()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MATERIAL_PREVIEW,
            draftId: draftId,
            isTemporary: true);
        asset.MarkProcessing();

        var target = ValidTargetEntity("material");
        var result = asset.BindTo(target);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_MaterialPreview_WithDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
            AssetUsageType.MATERIAL_PREVIEW,
            ValidFileName("cover.png"),
            ValidContentType("image/png"),
            2048,
            draftId: draftId,
            targetEntity: null,
            isTemporary: true);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(draftId, asset.DraftId);
        Assert.Null(asset.TargetEntity);
        Assert.True(asset.IsTemporary);
    }

    [Fact]
    public void Register_MaterialVideo_WithDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.VIDEO,
            AssetUsageType.MATERIAL_VIDEO,
            ValidFileName("video.mp4"),
            ValidContentType("video/mp4"),
            1024,
            draftId: draftId,
            targetEntity: null,
            isTemporary: true);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(draftId, asset.DraftId);
        Assert.Null(asset.TargetEntity);
        Assert.True(asset.IsTemporary);
    }

    [Fact]
    public void RegisterFromProvider_WithDraft_Succeeds()
    {
        var draftId = Guid.CreateVersion7();

        var result = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            AssetUsageType.MATERIAL_VIDEO,
            ValidFileName("video.mp4"),
            ValidContentType("video/mp4"),
            targetEntity: null,
            AssetStatus.PROCESSING,
            draftId: draftId);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal(draftId, asset.DraftId);
        Assert.Null(asset.TargetEntity);
        Assert.True(asset.IsTemporary);
    }

    [Fact]
    public void IsStale_PendingFileUpload_MatchesPendingUploadFile()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD, AssetUsageType.AVATAR);

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.PendingFileUpload));
    }

    [Fact]
    public void IsStale_PendingFileUpload_DoesNotMatchPendingUploadVideo()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD, AssetUsageType.MATERIAL_VIDEO);

        Assert.False(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.PendingFileUpload));
    }

    [Fact]
    public void IsStale_DraftFile_MatchesPendingUploadFileWithDraft()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            draftId: draftId,
            isTemporary: true);

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.DraftFile));
    }

    [Fact]
    public void IsStale_DraftFile_MatchesReadyFileWithDraft()
    {
        var draftId = Guid.CreateVersion7();
        var asset = CreateAsset(
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            draftId: draftId,
            isTemporary: true);
        asset.MarkReady();

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.DraftFile));
    }

    [Fact]
    public void IsStale_DraftFile_DoesNotMatchNonDraftFile()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD, AssetUsageType.AVATAR);

        Assert.False(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.DraftFile));
    }

    [Fact]
    public void IsStale_VideoUpload_MatchesPendingUploadVideo()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD, AssetUsageType.MATERIAL_VIDEO);

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.VideoUpload));
    }

    [Fact]
    public void IsStale_VideoUpload_DoesNotMatchPendingUploadFile()
    {
        var asset = CreateAssetInStatus(AssetStatus.PENDING_UPLOAD, AssetUsageType.AVATAR);

        Assert.False(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.VideoUpload));
    }

    [Fact]
    public void IsStale_VideoProcessing_MatchesProcessingVideo()
    {
        var asset = CreateAsset(usageType: AssetUsageType.MATERIAL_VIDEO);
        asset.MarkProcessing();

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.VideoProcessing));
    }

    [Fact]
    public void IsStale_VideoProcessing_DoesNotMatchProcessingFile()
    {
        var asset = CreateAssetInStatus(AssetStatus.PROCESSING, AssetUsageType.AVATAR);

        Assert.False(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.VideoProcessing));
    }

    [Fact]
    public void IsStale_Delete_MatchesDeletingAsset()
    {
        var asset = CreateAssetInStatus(AssetStatus.DELETING);

        Assert.True(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.Delete));
    }

    [Fact]
    public void IsStale_Delete_DoesNotMatchReadyAsset()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        Assert.False(asset.IsStale(DateTime.UtcNow, AssetStaleWorkflowType.Delete));
    }

    [Fact]
    public void IsContentReadable_Ready_ReturnsTrue()
    {
        var asset = CreateAssetInStatus(AssetStatus.READY);

        Assert.True(asset.IsContentReadable());
    }

    [Theory]
    [InlineData(AssetStatus.PENDING_UPLOAD)]
    [InlineData(AssetStatus.PROCESSING)]
    [InlineData(AssetStatus.FAILED)]
    [InlineData(AssetStatus.DELETING)]
    [InlineData(AssetStatus.DELETED)]
    public void IsContentReadable_NonReady_ReturnsFalse(AssetStatus status)
    {
        var asset = CreateAssetInStatus(status);

        Assert.False(asset.IsContentReadable());
    }

    [Fact]
    public void IsTombstoneVisible_Deleted_ReturnsTrue()
    {
        var asset = CreateAssetInStatus(AssetStatus.DELETED);

        Assert.True(asset.IsTombstoneVisible());
    }

    [Theory]
    [InlineData(AssetStatus.PENDING_UPLOAD)]
    [InlineData(AssetStatus.PROCESSING)]
    [InlineData(AssetStatus.READY)]
    [InlineData(AssetStatus.FAILED)]
    [InlineData(AssetStatus.DELETING)]
    public void IsTombstoneVisible_NonDeleted_ReturnsFalse(AssetStatus status)
    {
        var asset = CreateAssetInStatus(status);

        Assert.False(asset.IsTombstoneVisible());
    }

    private static FileName ValidFileName(string name = "test.jpg") =>
        FileName.Of(name).Value;

    private static MediaContentType ValidContentType(string type = "image/jpeg") =>
        MediaContentType.Of(type).Value;

    private static TargetEntity ValidTargetEntity(string type = "user", Guid? id = null) =>
        TargetEntity.Of(type, id ?? Guid.CreateVersion7()).Value;

    private static MediaAsset CreateAsset(
        AssetUsageType usageType = AssetUsageType.AVATAR,
        AssetKind kind = AssetKind.FILE,
        TargetEntity? targetEntity = null,
        Guid? draftId = null,
        bool isTemporary = false)
    {
        var fileName = usageType switch
        {
            AssetUsageType.MATERIAL_VIDEO or AssetUsageType.COURSE_VIDEO => ValidFileName("video.mp4"),
            AssetUsageType.MARKDOWN_FILE => ValidFileName("doc.pdf"),
            _ => ValidFileName("test.jpg"),
        };

        var contentType = usageType switch
        {
            AssetUsageType.MATERIAL_VIDEO or AssetUsageType.COURSE_VIDEO => ValidContentType("video/mp4"),
            AssetUsageType.MARKDOWN_FILE => ValidContentType("application/pdf"),
            _ => ValidContentType("image/jpeg"),
        };

        long size = 1024;

        if (targetEntity is null && !draftId.HasValue)
        {
            var policy = AssetUsagePolicyCatalog.Get(usageType).Value;
            if (policy.RegistrationMode == AssetRegistrationMode.EntityRequired)
            {
                string targetType = usageType switch
                {
                    AssetUsageType.AVATAR => "user",
                    AssetUsageType.COURSE_PREVIEW or AssetUsageType.COURSE_VIDEO => "course",
                    AssetUsageType.MATERIAL_VIDEO or AssetUsageType.MATERIAL_PREVIEW => "material",
                    _ => "material",
                };
                targetEntity = ValidTargetEntity(targetType);
            }
            else if (policy.RegistrationMode == AssetRegistrationMode.DraftOrEntity)
            {
                draftId = Guid.CreateVersion7();
                isTemporary = true;
            }
        }

        var actualKind = usageType switch
        {
            AssetUsageType.MATERIAL_VIDEO or AssetUsageType.COURSE_VIDEO => AssetKind.VIDEO,
            _ => AssetKind.FILE,
        };

        var result = MediaAsset.Register(
            Guid.CreateVersion7(),
            actualKind,
            usageType,
            fileName,
            contentType,
            size,
            draftId,
            targetEntity,
            isTemporary);

        Assert.True(
            result.IsSuccess,
            $"CreateAsset helper failed: {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        return result.Value;
    }

    private static MediaAsset CreateAssetInStatus(
        AssetStatus status,
        AssetUsageType usageType = AssetUsageType.AVATAR,
        TargetEntity? targetEntity = null,
        Guid? draftId = null,
        bool isTemporary = false)
    {
        var asset = CreateAsset(usageType, targetEntity: targetEntity, draftId: draftId, isTemporary: isTemporary);

        switch (status)
        {
            case AssetStatus.PENDING_UPLOAD:
                break;
            case AssetStatus.PROCESSING:
                asset.MarkProcessing();
                break;
            case AssetStatus.READY:
                asset.MarkReady();
                break;
            case AssetStatus.FAILED:
                asset.MarkFailed("test failure");
                break;
            case AssetStatus.DELETING:
                asset.RequestDelete();
                break;
            case AssetStatus.DELETED:
                asset.RequestDelete();
                asset.MarkDeleted();
                break;
        }

        Assert.Equal(status, asset.Status);
        return asset;
    }
}
