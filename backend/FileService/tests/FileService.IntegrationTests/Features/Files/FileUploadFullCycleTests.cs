using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class FileUploadFullCycleTests : FileServiceTestsBase
{
    public FileUploadFullCycleTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task CoursePreview_FullCycle_UploadAndComplete_PublishesBoundEvent()
    {
        // Arrange
        Guid courseId = Guid.NewGuid();
        byte[] fileContent = [1, 2, 3, 4];

        var request = new InitiateFileUploadRequest(
            FileName: "course-cover.png",
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", courseId));

        // Act — initiate
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);

        Assert.Equal("pending_upload", initiateResult.Status);
        Assert.NotEmpty(initiateResult.UploadUrl);

        // Act — upload to S3
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        // Act — complete
        OutboxCollector.Clear();

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        // Assert — FileAssetBound published
        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("course_preview", published.UsageType);
        Assert.Equal(courseId, published.TargetEntityId);
        Assert.Equal("course", published.TargetEntityType);

        // Assert — DB state
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(AssetUsageType.COURSE_PREVIEW, asset.UsageType);
            Assert.NotNull(asset.CompletedAt);
            Assert.NotNull(asset.TargetEntity);
            Assert.Equal(courseId, asset.TargetEntity.Id);
            Assert.Equal("course", asset.TargetEntity.Type);

            FileStorageRef storageRef = await db.FileStorageRefs.SingleAsync(x => x.AssetId == asset.Id);
            Assert.Equal($"files/{initiateResult.AssetId:N}.png", storageRef.StorageKey.Value);
        });
    }

    [Fact]
    public async Task MaterialPreview_FullCycle_UploadAndComplete_PublishesBoundEvent()
    {
        Guid lessonId = Guid.NewGuid();
        byte[] fileContent = [10, 20, 30, 40];

        var request = new InitiateFileUploadRequest(
            FileName: "lesson-thumb.webp",
            ContentType: "image/webp",
            Size: fileContent.Length,
            UsageType: "material_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("material", lessonId));

        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        OutboxCollector.Clear();

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("material_preview", published.UsageType);
        Assert.Equal(lessonId, published.TargetEntityId);
        Assert.Equal("material", published.TargetEntityType);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(AssetUsageType.MATERIAL_PREVIEW, asset.UsageType);
            Assert.NotNull(asset.CompletedAt);
            Assert.NotNull(asset.TargetEntity);
            Assert.Equal(lessonId, asset.TargetEntity.Id);
        });
    }

    [Fact]
    public async Task Avatar_FullCycle_UploadAndComplete_PublishesBoundEvent()
    {
        Guid userId = Guid.NewGuid();
        byte[] fileContent = [5, 6, 7, 8];

        var request = new InitiateFileUploadRequest(
            FileName: "avatar.jpg",
            ContentType: "image/jpeg",
            Size: fileContent.Length,
            UsageType: "avatar",
            DraftId: null,
            TargetEntity: new TargetEntityDto("user", userId));

        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        OutboxCollector.Clear();

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("avatar", published.UsageType);
        Assert.Equal(userId, published.TargetEntityId);
        Assert.Equal("user", published.TargetEntityType);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(AssetUsageType.AVATAR, asset.UsageType);
            Assert.Equal(userId, asset.TargetEntity!.Id);
        });
    }

    [Fact]
    public async Task MaterialPreview_FullCycle_UploadCompleteDelete_PublishesBothEvents()
    {
        Guid lessonId = Guid.NewGuid();
        byte[] fileContent = [1, 2, 3, 4];

        // Upload + complete
        var request = new InitiateFileUploadRequest(
            FileName: "preview.png",
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "material_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("material", lessonId));

        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        OutboxCollector.Clear();

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        completeResponse.EnsureSuccessStatusCode();

        FileAssetBound boundEvent = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal("material_preview", boundEvent.UsageType);

        // Delete
        OutboxCollector.Clear();

        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync($"/files/{initiateResult.AssetId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        FileAssetDeleted deletedEvent = OutboxCollector.OfType<FileAssetDeleted>().Single();
        Assert.Equal(initiateResult.AssetId, deletedEvent.AssetId);
        Assert.Equal("file", deletedEvent.Kind);
        Assert.Equal("material_preview", deletedEvent.UsageType);
        Assert.Equal(lessonId, deletedEvent.TargetEntityId);
        Assert.Equal("material", deletedEvent.TargetEntityType);

        // Phase 1 leaves the asset DELETING; phase 2 retention finalizes it.
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        await retention.ProcessDeletingAssetsAsync(CancellationToken.None);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task MaterialPreview_ReplacementDeletesPreviousAfterAggregateConfirmation()
    {
        Guid lessonId = Guid.NewGuid();
        byte[] fileContent = [1, 2, 3, 4];
        var target = new TargetEntityDto("material", lessonId);

        // Upload first preview
        var request1 = new InitiateFileUploadRequest(
            FileName: "old-preview.png",
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "material_preview",
            DraftId: null,
            TargetEntity: target);

        InitiateFileUploadResponse first = await InitiateFileUploadAsync(request1);
        await UploadToDirectUrlAsync(first.UploadUrl, first.RequiredHeaders, fileContent);

        HttpResponseMessage firstComplete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{first.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        firstComplete.EnsureSuccessStatusCode();
        long firstRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == first.AssetId)).BindingRevision);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(first.AssetId, firstRevision));

        // Upload second preview for same lesson
        var request2 = new InitiateFileUploadRequest(
            FileName: "new-preview.png",
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "material_preview",
            DraftId: null,
            TargetEntity: target);

        InitiateFileUploadResponse second = await InitiateFileUploadAsync(request2);
        await UploadToDirectUrlAsync(second.UploadUrl, second.RequiredHeaders, fileContent);

        HttpResponseMessage secondComplete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{second.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        secondComplete.EnsureSuccessStatusCode();

        long secondRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == second.AssetId)).BindingRevision);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(second.AssetId, secondRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(first.AssetId, firstRevision));

        // The aggregate confirmed the new revision before the old one became deletable.
        await ExecuteInDb(async db =>
        {
            MediaAsset oldAsset = await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId);
            Assert.Equal(AssetStatus.DELETING, oldAsset.Status);

            MediaAsset newAsset = await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId);
            Assert.Equal(AssetStatus.READY, newAsset.Status);
        });
    }

    [Fact]
    public async Task MarkdownFile_Excalidraw_StoredWithCanonicalExtension()
    {
        // .excalidraw is JSON; browsers send empty file.type. Frontend infers
        // "application/vnd.excalidraw+json" so it round-trips through the
        // catalog into a `.excalidraw` storage key.
        Guid materialId = Guid.NewGuid();
        byte[] fileContent = "{\"type\":\"excalidraw\",\"version\":2}"u8.ToArray();

        var request = new InitiateFileUploadRequest(
            FileName: "diagram.excalidraw",
            ContentType: "application/vnd.excalidraw+json",
            Size: fileContent.Length,
            UsageType: "markdown_file",
            DraftId: null,
            TargetEntity: new TargetEntityDto("material", materialId));

        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);
        Assert.Equal("pending_upload", initiateResult.Status);

        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(AssetUsageType.MARKDOWN_FILE, asset.UsageType);

            FileStorageRef storageRef = await db.FileStorageRefs.SingleAsync(x => x.AssetId == asset.Id);
            // canonical extension from policy catalog: vnd.excalidraw+json → .excalidraw
            Assert.EndsWith(".excalidraw", storageRef.StorageKey.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InitiateFileUpload_InvalidTargetEntityForUsageType_ReturnsBadRequest()
    {
        // Avatar requires "user" or "profile" target, not "course"
        var request = new InitiateFileUploadRequest(
            FileName: "avatar.jpg",
            ContentType: "image/jpeg",
            Size: 4,
            UsageType: "avatar",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InitiateFileUpload_EntityRequiredWithoutTarget_ReturnsBadRequest()
    {
        // course_preview requires targetEntity
        var request = new InitiateFileUploadRequest(
            FileName: "cover.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InitiateFileUpload_UnsupportedContentType_ReturnsBadRequest()
    {
        // course_preview only allows JPEG, PNG, WebP — not GIF
        var request = new InitiateFileUploadRequest(
            FileName: "animated.gif",
            ContentType: "image/gif",
            Size: 4,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        foreach ((string key, string value) in requiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(key, value);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
