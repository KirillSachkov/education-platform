using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services.AssetRegistry;
using FileService.Core.Services.Files;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BindDraftAssetsTests : FileServiceTestsBase
{
    public BindDraftAssetsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task InitiateMarkdownFileUpload_RequiresDraftId()
    {
        InitiateFileUploadRequest request = new(
            FileName: "inline.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "markdown_image",
            DraftId: null,
            TargetEntity: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BindDraftAssets_BindsOnlyRequestedAssets_AndPublishesEvents()
    {
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse first = await InitiateAndCompleteDraftFileAsync(draftId, "first.png");
        InitiateFileUploadResponse second = await InitiateAndCompleteDraftFileAsync(draftId, "second.png");

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", Guid.NewGuid()),
                [first.AssetId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(first.AssetId, published.AssetId);

        await ExecuteInDb(async db =>
        {
            MediaAsset firstAsset = await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId);
            MediaAsset secondAsset = await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId);

            Assert.False(firstAsset.IsTemporary);
            Assert.Null(firstAsset.DraftId);
            Assert.NotNull(firstAsset.TargetEntity);

            Assert.True(secondAsset.IsTemporary);
            Assert.Equal(draftId, secondAsset.DraftId);
            Assert.Null(secondAsset.TargetEntity);
        });
    }

    [Fact]
    public async Task InitiateMarkdownFileUpload_CreatesOpaqueStorageObjectKey()
    {
        Guid draftId = Guid.NewGuid();

        InitiateFileUploadResponse response = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: "inline.png",
                ContentType: "image/png",
                Size: 4,
                UsageType: "markdown_image",
                DraftId: draftId,
                TargetEntity: null));

        await ExecuteInDb(async db =>
        {
            FileStorageRef storageRef = await db.FileStorageRefs.SingleAsync(x => x.AssetId == response.AssetId);
            Assert.Equal($"files/{response.AssetId:N}.png", storageRef.StorageKey.Value);
        });
    }

    [Fact]
    public async Task BindDraftAssets_AsNonOwner_ShouldReturn403()
    {
        // User A (platform-author: has files.manage) initiates draft uploads.
        Guid userA = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();

        AuthenticateAs(userA, "platform-author");
        InitiateFileUploadResponse asset = await InitiateAndCompleteDraftFileAsync(draftId, "userA.png");

        // User B (also platform-author: has files.manage but is NOT admin) attempts to bind
        // User A's draft. The handler must return 403 with code draft.not.owner.
        Guid userB = Guid.NewGuid();
        AuthenticateAs(userB, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", Guid.NewGuid()),
                [asset.AssetId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("draft.not.owner", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindDraftAssets_RejectsAssetFromAnotherDraft()
    {
        InitiateFileUploadResponse first = await InitiateAndCompleteDraftFileAsync(Guid.NewGuid(), "first.png");
        Guid secondDraftId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                secondDraftId,
                new TargetEntityDto("material", Guid.NewGuid()),
                [first.AssetId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CleanupStaleDraftFiles_RequestsDeletionBeforeRetentionFinalizes()
    {
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse file = await InitiateAndCompleteDraftFileAsync(draftId, "stale.png");

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE media_assets SET created_at = {DateTime.UtcNow.AddDays(-2)} WHERE id = {file.AssetId}");
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        FileMaintenanceService service = scope.ServiceProvider.GetRequiredService<FileMaintenanceService>();

        int deleted = await service.CleanupStaleDraftFilesAsync(CancellationToken.None);

        Assert.Equal(1, deleted);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == file.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });

        await using AsyncServiceScope retentionScope = Services.CreateAsyncScope();
        AssetRetentionService retention =
            retentionScope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        Assert.Equal(1, await retention.ProcessDeletingAssetsAsync(CancellationToken.None));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == file.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task BindDraftAssets_MaterialPreview_IsRejectedAsSingleSlot()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(
            draftId,
            "cover.png");

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", materialId),
                [preview.AssetId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == preview.AssetId);
            Assert.True(asset.IsTemporary);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
            Assert.Equal(AssetUsageType.MATERIAL_PREVIEW, asset.UsageType);
        });
    }

    [Fact]
    public async Task BindDraftAssets_CrossAuthorSlotCollision_DoesNotDeleteVictimAsset()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        Guid victimDraftId = Guid.NewGuid();
        InitiateFileUploadResponse victim =
            await InitiateAndCompleteDraftPreviewAsync(victimDraftId, "victim-batch.png");
        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{victim.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId))))
            .EnsureSuccessStatusCode();

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        Guid attackerDraftId = Guid.NewGuid();
        InitiateFileUploadResponse attacker =
            await InitiateAndCompleteDraftPreviewAsync(attackerDraftId, "attacker-batch.png");
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind/",
            new BindDraftAssetsRequest(
                attackerDraftId,
                new TargetEntityDto("material", materialId),
                [attacker.AssetId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == victim.AssetId)).Status);
        });
    }

    [Fact]
    public async Task InitiateMaterialPreviewUpload_WithDraftId_Succeeds()
    {
        Guid draftId = Guid.NewGuid();

        InitiateFileUploadResponse response = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: "cover.png",
                ContentType: "image/png",
                Size: 4,
                UsageType: "material_preview",
                DraftId: draftId,
                TargetEntity: null));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == response.AssetId);
            Assert.Equal(AssetUsageType.MATERIAL_PREVIEW, asset.UsageType);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
            Assert.True(asset.IsTemporary);
        });
    }

    [Fact]
    public async Task BindDraftAssets_MaterialVideo_IsRejectedAsSingleSlot()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        // Initiate a draft video upload via /videos/uploads. The asset stays in
        // PendingUpload until Kinescope webhook/reconciliation marks it Ready —
        // for the bind test we promote it to Processing to simulate "video is
        // uploaded, provider is transcoding" which is the realistic case when
        // the user hits Save before the provider finishes.
        InitiateVideoUploadResponse videoResponse = await InitiateDraftVideoAsync(draftId, "draft.mp4");

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == videoResponse.AssetId);
            asset.MarkProcessing();
            await db.SaveChangesAsync();
        });

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", materialId),
                [videoResponse.AssetId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == videoResponse.AssetId);
            Assert.True(asset.IsTemporary);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
            Assert.Equal(AssetStatus.PROCESSING, asset.Status);
        });
    }

    [Fact]
    public async Task BindDraftAssets_ReadyMaterialVideo_DoesNotPublishReady()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse videoResponse = await InitiateDraftVideoAsync(draftId, "ready.mp4");

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == videoResponse.AssetId);
            asset.MarkProcessing();
            asset.MarkReady();
            await db.SaveChangesAsync();
        });
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind/",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", materialId),
                [videoResponse.AssetId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
    }

    [Fact]
    public async Task InitiateMaterialVideoUpload_WithDraftId_Succeeds()
    {
        Guid draftId = Guid.NewGuid();

        InitiateVideoUploadResponse response = await InitiateDraftVideoAsync(draftId, "draft.mp4");

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == response.AssetId);
            Assert.Equal(AssetUsageType.MATERIAL_VIDEO, asset.UsageType);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
            Assert.True(asset.IsTemporary);
        });
    }

    [Fact]
    public async Task CleanupPendingFileUploads_RequestsDeletionForStalePendingAssets()
    {
        Guid courseId = Guid.NewGuid();
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: "pending.png",
                ContentType: "image/png",
                Size: 4,
                UsageType: "course_preview",
                DraftId: null,
                TargetEntity: new TargetEntityDto("course", courseId)));

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE media_assets SET created_at = {DateTime.UtcNow.AddHours(-2)} WHERE id = {initiateResult.AssetId}");
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        FileMaintenanceService service = scope.ServiceProvider.GetRequiredService<FileMaintenanceService>();

        int deleted = await service.CleanupPendingFileUploadsAsync(CancellationToken.None);

        Assert.Equal(1, deleted);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteDraftFileAsync(Guid draftId, string fileName)
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: fileName,
                ContentType: "image/png",
                Size: 4,
                UsageType: "markdown_image",
                DraftId: draftId,
                TargetEntity: null));

        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        completeResponse.EnsureSuccessStatusCode();
        return initiateResult;
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteDraftPreviewAsync(Guid draftId, string fileName)
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: fileName,
                ContentType: "image/png",
                Size: 4,
                UsageType: "material_preview",
                DraftId: draftId,
                TargetEntity: null));

        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        completeResponse.EnsureSuccessStatusCode();
        return initiateResult;
    }

    private async Task<InitiateVideoUploadResponse> InitiateDraftVideoAsync(Guid draftId, string fileName)
    {
        var request = new InitiateVideoUploadRequest(
            FileName: fileName,
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: null,
            DraftId: draftId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        return envelope!.Result!;
    }

    private async Task<InitiateFileUploadResponse> InitiateFileUploadAsync(InitiateFileUploadRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        return envelope!.Result!;
    }

    private async Task UploadToDirectUrlAsync(
        string uploadUrl,
        IReadOnlyDictionary<string, string> requiredHeaders,
        byte[] content)
    {
        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(content),
        };

        if (requiredHeaders.TryGetValue("Content-Type", out string? contentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}